using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface ILoginAttemptRepository
{
    Task<LoginAttempt?> FindByEmailAsync(Email email, bool track = false, CancellationToken ct = default);
    Task AddAsync(LoginAttempt attempt, CancellationToken ct = default);
    void Update(LoginAttempt attempt);
    Task ResetByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);
    Task<int> DeleteStaleAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
