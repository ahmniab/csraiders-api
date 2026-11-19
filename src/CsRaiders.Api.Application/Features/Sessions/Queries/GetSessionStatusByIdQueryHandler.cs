using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Extensions;
using Mediator;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Features.Sessions.Queries;

public sealed record GetSessionStatusByIdQuery(string Id) : IRequest<SessionStatus>, IReadOnlyMessage;

public sealed class GetSessionStatusByIdQueryHandler(
    IDistributedCache cache,
    ISessionRepository repository) : IRequestHandler<GetSessionStatusByIdQuery, SessionStatus>
{
    public async ValueTask<SessionStatus> Handle(GetSessionStatusByIdQuery cmd, CancellationToken ct)
    {
        if (Guid.TryParse(cmd.Id, out var id))
        {
            var status = await cache.GetObjectAsync<SessionStatus>($"sid:{cmd.Id}");
            if (status != null) return status;

            var session = await repository.FindByIdAsync(id);
            if (session != null)
            {
                status = SessionStatus.FromSession(session);
                await cache.SetObjectAsync($"sid:{cmd.Id}", status);
                return status;
            }
        }

        throw new InvalidOperationException($"Session with id {cmd.Id} not found");
    }
}
