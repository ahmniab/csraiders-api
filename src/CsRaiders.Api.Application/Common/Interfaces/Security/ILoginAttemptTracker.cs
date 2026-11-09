using System.Net;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface ILoginAttemptTracker
{
    Task<LoginGate> EvaluateAsync(Email email, IPAddress? ip, DateTimeOffset now, CancellationToken ct = default);
    Task<LoginFailureOutcome> RegisterFailureAsync(Email email, IPAddress? ip, Guid? userId, DateTimeOffset now, CancellationToken ct = default);
    Task ResetAsync(Email email, IPAddress? ip, DateTimeOffset now, CancellationToken ct = default);
}
