using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Verto.Application.Abstractions.Persistence;
using Verto.Domain.Entities;

namespace Verto.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public async Task<IApplicationDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        return new ApplicationDbTransaction(transaction);
    }

    public Task AcquireRefreshTokenFamilyLockAsync(Guid familyId, CancellationToken cancellationToken = default)
    {
        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockKey.From(familyId)})",
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    private sealed class ApplicationDbTransaction(IDbContextTransaction transaction) : IApplicationDbTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

public static class AdvisoryLockKey
{
    public static long From(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }
}
