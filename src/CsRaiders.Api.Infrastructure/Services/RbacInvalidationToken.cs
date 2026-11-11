using CsRaiders.Api.Application.Common.Interfaces.Security;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class RbacInvalidationToken : IRbacInvalidationToken, IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _source = new();

    public CancellationToken CurrentToken
    {
        get { lock (_gate) return _source.Token; }
    }

    public void Invalidate()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _source;
            _source = new CancellationTokenSource();
        }
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) _source.Dispose();
    }
}
