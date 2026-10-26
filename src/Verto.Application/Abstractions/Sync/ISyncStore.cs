using Verto.Application.Dtos.Sync;

namespace Verto.Application.Abstractions.Sync;

public interface ISyncStore
{
    /// <summary>
    /// Upserts every change in one transaction and returns one outcome per change,
    /// in the same order as <paramref name="changes"/>.
    /// </summary>
    Task<IReadOnlyList<UpsertOutcome>> UpsertBatchAsync(
        Guid userId,
        IReadOnlyList<SyncChangeIn> changes,
        CancellationToken ct);

    /// <summary>Returns up to <paramref name="limit"/> records with server_version &gt; since, ascending.</summary>
    Task<IReadOnlyList<SyncChangeOut>> ReadSinceAsync(
        Guid userId,
        long since,
        int limit,
        CancellationToken ct);
}

public enum UpsertStatus
{
    Stored,

    /// <summary>The stored record was edited later than this change, so it was kept.</summary>
    Stale,

    /// <summary>Postgres could not store the payload (e.g. a NUL in jsonb text).</summary>
    Unstorable,
}

public sealed record UpsertOutcome(UpsertStatus Status, long? Version = null);
