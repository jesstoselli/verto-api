using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Verto.Application.Abstractions.Sync;
using Verto.Application.Dtos.Sync;
using Verto.Infrastructure.Persistence;

namespace Verto.Infrastructure.Sync;

public sealed class SyncStore : ISyncStore
{
    // nextval runs even on conflict, so EXCLUDED.server_version carries the new version.
    private const string UpsertSql = """
        INSERT INTO sync_records
            (user_id, entity_type, id, payload, deleted_at, client_updated_at, server_version, received_at)
        VALUES ($1, $2, $3, $4, $5, $6, nextval('sync_version_seq'), now())
        ON CONFLICT (user_id, entity_type, id) DO UPDATE SET
            payload = EXCLUDED.payload,
            deleted_at = EXCLUDED.deleted_at,
            client_updated_at = EXCLUDED.client_updated_at,
            server_version = EXCLUDED.server_version,
            received_at = EXCLUDED.received_at
        RETURNING server_version
        """;

    private const string ReadSql = """
        SELECT entity_type, id, payload::text, deleted_at, server_version
        FROM sync_records
        WHERE user_id = $1 AND server_version > $2
        ORDER BY server_version
        LIMIT $3
        """;

    private readonly NpgsqlDataSource _dataSource;

    public SyncStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // Class 22 = data exception: content System.Text.Json accepts but jsonb does not.
    private const string DataExceptionClass = "22";

    public async Task<IReadOnlyList<long?>> UpsertBatchAsync(
        Guid userId,
        IReadOnlyList<SyncChangeIn> changes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(changes);

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Pushes for one user are serialized so their versions commit in order;
        // otherwise a pull could advance its cursor past a still-uncommitted lower version.
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", connection, transaction))
        {
            lockCommand.Parameters.Add(new NpgsqlParameter { Value = AdvisoryLockKey.From(userId) });
            await lockCommand.ExecuteNonQueryAsync(ct);
        }

        var versions = new List<long?>(changes.Count);
        foreach (var change in changes)
        {
            // One unstorable record must not roll back the rest of the batch, or the
            // client's outbox would retry the same poisoned push forever.
            await transaction.SaveAsync("change", ct);

            await using var command = new NpgsqlCommand(UpsertSql, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter { Value = userId });
            command.Parameters.Add(new NpgsqlParameter { Value = change.EntityType });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Payload.GetRawText(), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new NpgsqlParameter
            {
                Value = change.DeletedAt is { } deletedAt ? (object)deletedAt.UtcDateTime : DBNull.Value,
                NpgsqlDbType = NpgsqlDbType.TimestampTz,
            });
            command.Parameters.Add(new NpgsqlParameter
            {
                Value = change.ClientUpdatedAt.UtcDateTime,
                NpgsqlDbType = NpgsqlDbType.TimestampTz,
            });

            try
            {
                var version = await command.ExecuteScalarAsync(ct);
                versions.Add((long)version!);
            }
            catch (PostgresException ex) when (ex.SqlState.StartsWith(DataExceptionClass, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync("change", ct);
                versions.Add(null);
            }
        }

        await transaction.CommitAsync(ct);
        return versions;
    }

    public async Task<IReadOnlyList<SyncChangeOut>> ReadSinceAsync(
        Guid userId,
        long since,
        int limit,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(ReadSql);
        command.Parameters.Add(new NpgsqlParameter { Value = userId });
        command.Parameters.Add(new NpgsqlParameter { Value = since });
        command.Parameters.Add(new NpgsqlParameter { Value = limit });

        var rows = new List<SyncChangeOut>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            using var payload = JsonDocument.Parse(reader.GetString(2));
            rows.Add(new SyncChangeOut(
                EntityType: reader.GetString(0),
                Id: reader.GetGuid(1),
                Payload: payload.RootElement.Clone(),
                DeletedAt: reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                ServerVersion: reader.GetInt64(4)));
        }

        return rows;
    }
}
