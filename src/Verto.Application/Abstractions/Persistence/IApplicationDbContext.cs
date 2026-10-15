using Microsoft.EntityFrameworkCore;
using Verto.Domain.Entities;

namespace Verto.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<IApplicationDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task AcquireRefreshTokenFamilyLockAsync(Guid familyId, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IApplicationDbTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
