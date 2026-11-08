namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public enum HashOutcome { Success, Unavailable }
public readonly record struct PasswordHashResult(HashOutcome Outcome, string? Hash)
{
    public static PasswordHashResult Unavailable => new(HashOutcome.Unavailable, null);
    public static PasswordHashResult Succeeded(string hash) => new(HashOutcome.Success, hash);
}
public readonly record struct PasswordVerifyResult(bool IsValid, bool NeedsRehash, HashOutcome Outcome = HashOutcome.Success);

public interface IPasswordHasher
{
    Task<PasswordHashResult> HashAsync(string password, CancellationToken ct = default);
    Task<PasswordVerifyResult> VerifyAsync(string password, string? encodedHash, CancellationToken ct = default);
}
