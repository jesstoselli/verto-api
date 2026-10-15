using Verto.Application.Abstractions.Sync;
using Verto.Application.Common;
using Verto.Application.Dtos.Sync;

namespace Verto.Application.UseCases.Sync;

public sealed class SyncService
{
    public const int MaxBatchSize = 500;
    public const int DefaultPullLimit = 500;
    public const int MaxPullLimit = 1000;

    private readonly ISyncStore _store;

    public SyncService(ISyncStore store)
    {
        _store = store;
    }

    public async Task<OperationResult<PushResponse>> PushAsync(
        Guid userId,
        PushRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Changes is null || request.Changes.Count == 0)
        {
            return OperationResult.Fail<PushResponse>("validation_error", "Envie ao menos uma mudança.");
        }

        if (request.Changes.Count > MaxBatchSize)
        {
            return OperationResult.Fail<PushResponse>("validation_error", "Máximo de 500 mudanças por push.");
        }

        var accepted = new List<SyncChangeIn>(request.Changes.Count);
        var rejected = new List<PushRejected>();

        foreach (var change in request.Changes)
        {
            if (change is null)
            {
                rejected.Add(new PushRejected(string.Empty, Guid.Empty, "invalid_change"));
                continue;
            }

            var error = SyncEntityCatalog.Validate(change);
            if (error is null)
            {
                accepted.Add(change);
            }
            else
            {
                rejected.Add(new PushRejected(change.EntityType ?? string.Empty, change.Id, error));
            }
        }

        IReadOnlyList<long> versions = accepted.Count == 0
            ? []
            : await _store.UpsertBatchAsync(userId, accepted, ct);

        var acceptedOut = accepted
            .Select((change, index) => new PushAccepted(change.EntityType, change.Id, versions[index]))
            .ToList();

        return OperationResult.Ok(new PushResponse(acceptedOut, rejected));
    }
}
