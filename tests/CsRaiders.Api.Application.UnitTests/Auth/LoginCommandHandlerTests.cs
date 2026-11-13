using System.Net;
using System.Text.Json;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Application.Features.Auth.Commands.Login;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CsRaiders.Api.Application.UnitTests.Auth;

public sealed class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly Email Mail = Email.Create("user@example.com");
    private const string Hash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn";

    [Fact]
    public async Task Handle_resolves_role_permissions_populates_perms_mask_cache_and_passes_bitmask_to_token_service()
    {
        var role = Role.Create("Manager", "manager", RoleScope.Staff, [PermissionCatalog.CourseRead, PermissionCatalog.RoleManage]);
        var user = User.RegisterVerified(Mail, "Manager User", Hash, Now);
        user.AssignRole(role.Id, null, Now);

        var tokens = new TestTokenService();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var invalidation = new TestRbacInvalidationToken();

        var handler = CreateHandler(user, [role], tokens, cache, invalidation);

        var command = new LoginCommand("user@example.com", "Password123!", IPAddress.Loopback, "fingerprint-1", "test-agent");
        var result = await handler.Handle(command, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        tokens.LastBitmask.Should().NotBeNull();

        ulong expectedBitmask = (1UL << 0) | (1UL << 9); // CourseRead (bit 0) | RoleManage (bit 9)
        tokens.LastBitmask.Should().Be(expectedBitmask);

        var cacheKey = $"perms_mask:{user.Id:D}";
        cache.TryGetValue(cacheKey, out ulong cachedBitmask).Should().BeTrue();
        cachedBitmask.Should().Be(expectedBitmask);
    }

    [Fact]
    public async Task Handle_applies_permission_overrides_to_role_permissions_accurately()
    {
        var role = Role.Create("Tutor", "tutor", RoleScope.Staff, [PermissionCatalog.CourseRead, PermissionCatalog.CourseWrite]);
        var user = User.RegisterVerified(Mail, "Tutor User", Hash, Now);
        user.AssignRole(role.Id, null, Now);
        user.SetPermissionOverride(PermissionCatalog.CourseWrite, PermissionEffect.Deny, Guid.NewGuid(), Now);
        user.SetPermissionOverride(PermissionCatalog.PaymentRefund, PermissionEffect.Grant, Guid.NewGuid(), Now);

        var tokens = new TestTokenService();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var invalidation = new TestRbacInvalidationToken();

        var handler = CreateHandler(user, [role], tokens, cache, invalidation);

        var command = new LoginCommand("user@example.com", "Password123!", IPAddress.Loopback, "fingerprint-1", "test-agent");
        var result = await handler.Handle(command, CancellationToken.None);

        result.Succeeded.Should().BeTrue();

        // Effective: CourseRead (bit 0) and PaymentRefund (bit 12), with CourseWrite (bit 1) denied
        ulong expectedBitmask = (1UL << 0) | (1UL << 12);
        tokens.LastBitmask.Should().Be(expectedBitmask);

        var cacheKey = $"perms_mask:{user.Id:D}";
        cache.TryGetValue(cacheKey, out ulong cachedBitmask).Should().BeTrue();
        cachedBitmask.Should().Be(expectedBitmask);
    }

    [Fact]
    public async Task Handle_with_no_roles_or_permissions_emits_and_caches_zero_bitmask()
    {
        var user = User.RegisterVerified(Mail, "Plain User", Hash, Now);

        var tokens = new TestTokenService();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var invalidation = new TestRbacInvalidationToken();

        var handler = CreateHandler(user, [], tokens, cache, invalidation);

        var command = new LoginCommand("user@example.com", "Password123!", IPAddress.Loopback, "fingerprint-1", "test-agent");
        var result = await handler.Handle(command, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        tokens.LastBitmask.Should().Be(0UL);

        var cacheKey = $"perms_mask:{user.Id:D}";
        cache.TryGetValue(cacheKey, out ulong cachedBitmask).Should().BeTrue();
        cachedBitmask.Should().Be(0UL);
    }

    [Fact]
    public async Task Cache_entry_is_evicted_when_invalidation_token_is_triggered()
    {
        var role = Role.Create("Student", "student", RoleScope.Student, [PermissionCatalog.CourseRead]);
        var user = User.RegisterVerified(Mail, "Student User", Hash, Now);
        user.AssignRole(role.Id, null, Now);

        var tokens = new TestTokenService();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var invalidation = new TestRbacInvalidationToken();

        var handler = CreateHandler(user, [role], tokens, cache, invalidation);

        var command = new LoginCommand("user@example.com", "Password123!", IPAddress.Loopback, "fingerprint-1", "test-agent");
        await handler.Handle(command, CancellationToken.None);

        var cacheKey = $"perms_mask:{user.Id:D}";
        cache.TryGetValue(cacheKey, out ulong _).Should().BeTrue();

        // Invalidate RBAC token
        invalidation.Invalidate();

        cache.TryGetValue(cacheKey, out ulong _).Should().BeFalse("Cache entry should be evicted when invalidation token is cancelled");
    }

    [Fact]
    public async Task Handle_with_invalid_credentials_does_not_issue_token_or_populate_cache()
    {
        var user = User.RegisterVerified(Mail, "User", Hash, Now);
        var tokens = new TestTokenService();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var invalidation = new TestRbacInvalidationToken();

        var handler = CreateHandler(user, [], tokens, cache, invalidation, passwordValid: false);

        var command = new LoginCommand("user@example.com", "WrongPassword", IPAddress.Loopback, "fingerprint-1", "test-agent");
        var result = await handler.Handle(command, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        tokens.LastBitmask.Should().BeNull();

        var cacheKey = $"perms_mask:{user.Id:D}";
        cache.TryGetValue(cacheKey, out ulong _).Should().BeFalse();
    }

    private static LoginCommandHandler CreateHandler(
        User user,
        Role[] roles,
        TestTokenService tokens,
        IMemoryCache cache,
        IRbacInvalidationToken invalidation,
        bool passwordValid = true)
    {
        var usersRepo = new TestUserRepository(user);
        var rolesRepo = new TestRoleRepository(roles);
        var gates = new TestGateTracker();
        var passwords = new TestPasswordHasher(passwordValid);
        var sessions = new TestSessionRepository();
        var settings = new TestSettingsProvider();
        var audit = new TestAuditRepo();
        var clock = new TestClock(Now);
        var db = new TestDbContext();
        var sessionCache = new TestSessionCache();

        return new LoginCommandHandler(
            usersRepo,
            gates,
            passwords,
            tokens,
            sessions,
            settings,
            audit,
            clock,
            db,
            sessionCache,
            rolesRepo,
            cache,
            invalidation);
    }

    private sealed class TestTokenService : ITokenService
    {
        public Guid? LastUserId { get; private set; }
        public Guid? LastSessionId { get; private set; }
        public ulong? LastBitmask { get; private set; }
        public DateTimeOffset? LastNow { get; private set; }

        public string CreateAccessToken(Guid userId, Guid sessionId, ulong permissionsBitmask, DateTimeOffset now)
        {
            LastUserId = userId;
            LastSessionId = sessionId;
            LastBitmask = permissionsBitmask;
            LastNow = now;
            return "mock_access_token";
        }

        public string CreateRefreshToken(Guid userId, Guid sessionId, DateTimeOffset now) => "mock_refresh_token";

        public bool TryValidateRefreshToken(string token, DateTimeOffset now, out RefreshTokenClaims claims)
        {
            claims = new RefreshTokenClaims(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            return true;
        }

        public byte[] HashRefreshToken(string token) =>
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
    }

    private sealed class TestRbacInvalidationToken : IRbacInvalidationToken
    {
        private CancellationTokenSource _cts = new();
        public CancellationToken CurrentToken => _cts.Token;

        public void Invalidate()
        {
            var prev = _cts;
            _cts = new CancellationTokenSource();
            prev.Cancel();
            prev.Dispose();
        }
    }

    private sealed class TestUserRepository(User? user = null) : IUserRepository
    {
        public Task<User?> FindByEmailAsync(Email email, bool track = false, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(user);
        public Task<User?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(user?.Id == id ? user : null);
        public Task<User?> FindByIdWithPermissionsAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(user?.Id == id ? user : null);
        public Task<(bool HasSessionMulti, bool IsStudent)> GetAuthenticationPolicyAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult((false, false));
        public Task<IReadOnlyList<Guid>> FindIdsByRoleIdAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task AcquireLoginLockAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(Email email, CancellationToken ct = default) =>
            Task.FromResult(user is not null);
        public Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(User user) { }
    }

    private sealed class TestRoleRepository(params Role[] roles) : IRoleRepository
    {
        public Task<Role?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(roles.SingleOrDefault(r => r.Id == id));
        public Task<Role?> FindBySlugAsync(string slug, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(roles.SingleOrDefault(r => r.Slug == slug));
        public Task<IReadOnlyList<Role>> FindAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(roles);
        public Task<IReadOnlyList<Role>> FindByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(roles.Where(r => ids.Contains(r.Id)).ToArray());
        public Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(roles);
        public Task<int> CountUsersAsync(Guid roleId, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddAsync(Role role, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(Role role) { }
        public void Delete(Role role) { }
    }

    private sealed class TestSessionRepository : ISessionRepository
    {
        public readonly List<Session> Sessions = [];
        public Task<Session?> FindByIdAsync(Guid sessionId, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.Id == sessionId));
        public Task<Session?> FindByRefreshHashAsync(byte[] hash, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.RefreshTokenHash.SequenceEqual(hash)));
        public Task<Session?> FindByRefreshHashForUpdateAsync(byte[] hash, CancellationToken ct = default) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.RefreshTokenHash.SequenceEqual(hash)));
        public Task<Session?> FindByIdForUpdateAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.Id == sessionId));
        public Task<IReadOnlyList<Session>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, bool track = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Session>>(Sessions.Where(s => s.UserId == userId).ToArray());
        public Task<int> CountActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, CancellationToken ct = default) =>
            Task.FromResult(Sessions.Count(s => s.UserId == userId));
        public Task<IReadOnlyList<SessionSummary>> ListByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionSummary>>([]);
        public Task AddAsync(Session session, CancellationToken ct = default)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }
        public void Update(Session session) { }
        public Task<int> BulkRevokeByUserIdAsync(Guid userId, SessionRevokeReason reason, Guid? exceptSessionId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RevokeOldestNAsync(Guid userId, int count, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RevokeAllByUserIdAsync(Guid userId, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task SetEnforceSingleSessionAsync(Guid userId, bool enforce, DateTimeOffset now, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset now, DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class TestGateTracker : ILoginAttemptTracker
    {
        public Task<LoginGate> EvaluateAsync(Email email, IPAddress? ip, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(LoginGate.Open);
        public Task<LoginFailureOutcome> RegisterFailureAsync(Email email, IPAddress? ip, Guid? userId, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(LoginFailureOutcome.Recorded);
        public Task ResetAsync(Email email, IPAddress? ip, DateTimeOffset now, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestPasswordHasher(bool isValid = true) : IPasswordHasher
    {
        public Task<PasswordHashResult> HashAsync(string password, CancellationToken ct = default) =>
            Task.FromResult(PasswordHashResult.Succeeded("new_hash"));
        public Task<PasswordVerifyResult> VerifyAsync(string password, string? encodedHash, CancellationToken ct = default) =>
            Task.FromResult(new PasswordVerifyResult(isValid, false, HashOutcome.Success));
    }

    private sealed class TestSettingsProvider : IAppSettingsProvider
    {
        public Task<int> GetMultiSessionCapAsync(CancellationToken ct = default) => Task.FromResult(3);
        public void EvictMultiSessionCap() { }
    }

    private sealed class TestAuditRepo : ISecurityEventRepository
    {
        public Task AddAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null, JsonDocument? details = null, DateTimeOffset? createdAt = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class TestClock(DateTimeOffset now) : IPlatformClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class TestSessionCache : ISessionCacheHook
    {
        public void Evict(Guid sessionId) { }
    }

    private sealed class TestDbContext : IApplicationDbContext
    {
        public DbSet<User> Users => null!;
        public DbSet<Role> Roles => null!;
        public DbSet<UserRole> UserRoles => null!;
        public DbSet<UserPermission> UserPermissions => null!;
        public DbSet<Session> Sessions => null!;
        public DbSet<LoginAttempt> LoginAttempts => null!;
        public DbSet<PendingRegistration> PendingRegistrations => null!;
        public DbSet<PasswordResetToken> PasswordResetTokens => null!;
        public DbSet<SecurityEvent> SecurityEvents => null!;
        public DbSet<AppSetting> AppSettings => null!;
        public EntityEntry Entry(object entity) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
