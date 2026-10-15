using Verto.Application.Dtos.Sync;

namespace Verto.Application.Abstractions.Sync;

public interface ISyncStore
{
    /// <summary>
    /// Upserts every change in one transaction and returns the assigned
    /// server versions in the same order as <paramref name="changes"/>.
    /// </summary>
    Task<IReadOnlyList<long>> UpsertBatchAsync(
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
