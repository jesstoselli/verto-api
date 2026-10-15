namespace Verto.Domain.Entities;

/// <summary>
/// One client-owned record. The server stores the payload opaquely because
/// planner rules live on the client (offline-first).
/// </summary>
public class SyncRecord
{
    public Guid UserId { get; set; }
    public string EntityType { get; set; } = null!;
    public Guid Id { get; set; }
    public string Payload { get; set; } = null!;
    public DateTime? DeletedAt { get; set; }
    public DateTime ClientUpdatedAt { get; set; }
    public long ServerVersion { get; set; }
    public DateTime ReceivedAt { get; set; }
}
