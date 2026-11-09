using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using System.Net;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface ILoginAttemptRepository
{
    Task<LoginAttempt?> FindByEmailAsync(Email email, bool track = false, CancellationToken ct = default);
    Task<LoginAttempt?> FindByEmailAndIpAsync(Email email, IPAddress? ip, bool track = false, CancellationToken ct = default);
    Task<int> CountRecentFailuresByEmailAsync(Email email, DateTimeOffset since, CancellationToken ct = default);
    Task AddAsync(LoginAttempt attempt, CancellationToken ct = default);
    void Update(LoginAttempt attempt);
    Task ResetByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);
    Task<int> DeleteStaleAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
