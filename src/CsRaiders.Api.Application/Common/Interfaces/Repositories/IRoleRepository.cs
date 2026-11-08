using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<Role?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default);
    Task<Role?> FindBySlugAsync(string slug, bool track = false, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> FindAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Role>> FindByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct = default);

    Task AddAsync(Role role, CancellationToken ct = default);
    void Update(Role role);
}
