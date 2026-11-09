using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(Email email, bool track = false, bool includeDeleted = false, CancellationToken ct = default);
    Task<User?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default);

    /// <summary>Loads User + Roles + PermissionOverrides in one split query for permission resolution.</summary>
    Task<User?> FindByIdWithPermissionsAsync(Guid id, bool track = false, CancellationToken ct = default);
    Task<(bool HasSessionMulti, bool IsStudent)> GetAuthenticationPolicyAsync(Guid userId, CancellationToken ct = default);
    Task AcquireLoginLockAsync(Guid userId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Email email, CancellationToken ct = default);

    /// <summary>Keyset-paginated admin list. Filters: status, roleId, name/email search string.</summary>
    Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken ct = default);

    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
}
