using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface IPasswordResetTokenRepository
{
    Task<PasswordResetToken?> FindByTokenHashAsync(byte[] tokenHash, bool track = false, CancellationToken ct = default);
    Task<IReadOnlyList<PasswordResetToken>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);
    Task<int> CountIssuedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);
    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);
    void Update(PasswordResetToken token);
    Task InvalidateAllByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);
    Task<int> DeleteStaleAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
