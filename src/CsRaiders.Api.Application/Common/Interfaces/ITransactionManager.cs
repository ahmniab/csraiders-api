namespace CsRaiders.Api.Application.Common.Interfaces;

public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public interface ITransactionManager
{
    Task<ITransactionScope> BeginAsync(CancellationToken ct = default);
}
