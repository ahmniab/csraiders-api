using CsRaiders.Api.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace CsRaiders.Api.Infrastructure.Persistence;

public sealed class EfTransactionManager(AppDbContext db) : ITransactionManager
{
    public async Task<ITransactionScope> BeginAsync(CancellationToken ct = default) =>
        new EfTransactionScope(await db.Database.BeginTransactionAsync(ct));

    private sealed class EfTransactionScope(IDbContextTransaction transaction) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => transaction.RollbackAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
