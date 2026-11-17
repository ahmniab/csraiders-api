using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Domain.Entities;
using Mediator;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.Features.Sessions.Queries;

public sealed record GetSessionStatusByIdQuery(string Id) : IRequest<SessionStatus>;

public sealed class GetSessionStatusByIdQueryHandler(
    IDistributedCache cache,
    ISessionRepository repository) : IRequestHandler<GetSessionStatusByIdQuery, SessionStatus>
{
    public async ValueTask<SessionStatus> Handle(GetSessionStatusByIdQuery cmd, CancellationToken ct)
    {
        if (Guid.TryParse(cmd.Id, out var id))
        {
            var session = await cache.GetObjectAsync<Session>($"sid:{cmd.Id}");
            if (session != null)
            {
                await cache.SetObjectAsync($"sid:{cmd.Id}", session);
                return SessionStatus.FromSession(session);
            }

            session =
                await repository.FindByIdAsync(id);
            if (session != null)
            {
                await cache.SetObjectAsync($"sid:{cmd.Id}", session);
                return SessionStatus.FromSession(session);
            }
            
        }

        throw new InvalidOperationException($"Session with id {cmd.Id} not found");
    }
}