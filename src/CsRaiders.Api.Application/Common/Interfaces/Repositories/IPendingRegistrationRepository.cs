using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface IPendingRegistrationRepository
{
    Task<PendingRegistration?> FindByEmailAsync(Email email, bool track = false, CancellationToken ct = default);
    Task AddAsync(PendingRegistration pending, CancellationToken ct = default);
    void Update(PendingRegistration pending);
    Task DeleteByEmailAsync(Email email, CancellationToken ct = default);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct = default);
}
