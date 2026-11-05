using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
