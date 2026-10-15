using System.Text.Json;

namespace Verto.Application.Dtos.Sync;

public sealed record SyncChangeIn(
    string EntityType,
    Guid Id,
    JsonElement Payload,
    DateTimeOffset? DeletedAt,
    DateTimeOffset ClientUpdatedAt);

public sealed record PushRequest(IReadOnlyList<SyncChangeIn> Changes);

public sealed record PushAccepted(string EntityType, Guid Id, long ServerVersion);

public sealed record PushRejected(string EntityType, Guid Id, string Error);

public sealed record PushResponse(IReadOnlyList<PushAccepted> Accepted, IReadOnlyList<PushRejected> Rejected);

public sealed record SyncChangeOut(
    string EntityType,
    Guid Id,
    JsonElement Payload,
    DateTimeOffset? DeletedAt,
    long ServerVersion);

public sealed record PullResponse(IReadOnlyList<SyncChangeOut> Changes, long Cursor, bool HasMore);
