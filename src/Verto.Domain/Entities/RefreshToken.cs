namespace Verto.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }

    /// <summary>SHA-256 of the opaque token sent to the client; the raw token is never stored.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
