using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Models;
using Mediator;

namespace CsRaiders.Api.Application.Features.Sessions.Queries;

public sealed record GetSessionSummariesQuery(Guid UserId) : IRequest<IReadOnlyList<SessionSummary>>;

public sealed class GetSessionSummariesQueryHandler(
    ISessionRepository sessions,
    IPlatformClock clock) : IRequestHandler<GetSessionSummariesQuery, IReadOnlyList<SessionSummary>>
{
    public async ValueTask<IReadOnlyList<SessionSummary>> Handle(GetSessionSummariesQuery request, CancellationToken ct) =>
        await sessions.ListByUserIdAsync(request.UserId, clock.UtcNow, ct);
}
