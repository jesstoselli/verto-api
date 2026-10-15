using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Verto.Domain.Entities;

namespace Verto.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapped for migrations only; reads and writes go through <c>SyncStore</c> with explicit SQL.
/// </summary>
public class SyncRecordConfiguration : IEntityTypeConfiguration<SyncRecord>
{
    public void Configure(EntityTypeBuilder<SyncRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sync_records");
        // user_id in the key: the same client UUID from two accounts can never collide.
        builder.HasKey(x => new { x.UserId, x.EntityType, x.Id });
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(32);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.ClientUpdatedAt).HasColumnName("client_updated_at");
        builder.Property(x => x.ServerVersion).HasColumnName("server_version");
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at");

        builder.HasIndex(x => new { x.UserId, x.ServerVersion }).HasDatabaseName("ix_sync_records_user_version");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
