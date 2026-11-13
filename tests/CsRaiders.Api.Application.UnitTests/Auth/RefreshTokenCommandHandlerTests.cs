using System.Net;
using System.Text.Json;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Application.Features.Auth.Commands.Refresh;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CsRaiders.Api.Application.UnitTests.Auth;

public sealed class RefreshTokenCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly Email Mail = Email.Create("user@example.com");
    private const string Hash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_with_missing_refresh_token_fails_fast_with_invalid_grant_without_querying_session_or_permissions(string? token)
    {
        var (handler, context) = CreateHandler();

        var result = await handler.Handle(new RefreshTokenCommand(token, "fingerprint-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_grant");
        context.Sessions.FindByIdForUpdateCount.Should().Be(0);
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_with_missing_device_fingerprint_fails_fast_with_session_superseded_without_querying_session_or_permissions(string? fingerprint)
    {
        var (handler, context) = CreateHandler();

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", fingerprint), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("session_superseded");
        context.Sessions.FindByIdForUpdateCount.Should().Be(0);
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_with_cryptographically_invalid_token_fails_fast_without_querying_session_or_permissions()
    {
        var (handler, context) = CreateHandler();
        context.Tokens.ValidationResult = false;

        var result = await handler.Handle(new RefreshTokenCommand("invalid_jwt_token", "fingerprint-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_grant");
        context.Sessions.FindByIdForUpdateCount.Should().Be(0);
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_when_session_not_found_fails_fast_without_querying_permissions()
    {
        var (handler, context) = CreateHandler();
        // No session added to context.Sessions

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", "fingerprint-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_grant");
        context.Sessions.FindByIdForUpdateCount.Should().Be(1);
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_when_session_userId_mismatches_token_userId_fails_fast_without_querying_permissions()
    {
        var tokenUserId = Guid.NewGuid();
        var sessionUserId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var (handler, context) = CreateHandler();
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(tokenUserId, sessionId, Guid.NewGuid());

        var session = Session.Open(sessionId, sessionUserId, context.Tokens.HashRefreshToken("valid_token"), "fingerprint-1", null, null, false, false, Now);
        context.Sessions.Sessions.Add(session);

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", "fingerprint-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_grant");
        context.Sessions.FindByIdForUpdateCount.Should().Be(1);
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_when_device_fingerprint_mismatches_fails_fast_with_session_superseded_without_querying_permissions()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var (handler, context) = CreateHandler();
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(userId, sessionId, Guid.NewGuid());

        var session = Session.Open(sessionId, userId, context.Tokens.HashRefreshToken("valid_token"), "registered-device-1", null, null, false, false, Now);
        context.Sessions.Sessions.Add(session);

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", "different-device-2"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("session_superseded");
        context.Sessions.FindByIdForUpdateCount.Should().Be(1);
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_when_token_reuse_detected_updates_session_records_audit_and_fails_fast_without_querying_permissions()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var (handler, context) = CreateHandler();
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(userId, sessionId, Guid.NewGuid());

        var initialToken = "token_generation_1";
        var rotatedToken = "token_generation_2";
        var replayedToken = "token_generation_1";

        var session = Session.Open(sessionId, userId, context.Tokens.HashRefreshToken(initialToken), "device-1", null, null, false, false, Now);
        context.Sessions.Sessions.Add(session);

        // First rotation: valid
        var rotateOutcome = session.TryRotate(
            context.Tokens.HashRefreshToken(initialToken),
            context.Tokens.HashRefreshToken(rotatedToken),
            "device-1",
            Now);
        rotateOutcome.Should().Be(RefreshOutcome.Rotated);

        // Replay of initialToken beyond the 15-second grace window (e.g. +30 seconds)
        context.Clock.Now = Now.AddSeconds(30);

        var result = await handler.Handle(new RefreshTokenCommand(replayedToken, "device-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("session_compromised");
        context.Sessions.UpdatedSessions.Should().Contain(session);
        context.Audit.Events.Should().Contain(e => e.EventName == "token_reuse_detected" && e.UserId == userId);
        context.SessionCache.EvictedSessions.Should().Contain(sessionId);
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_when_session_is_inactive_fails_fast_with_session_superseded_without_querying_permissions()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var (handler, context) = CreateHandler();
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(userId, sessionId, Guid.NewGuid());

        var session = Session.Open(sessionId, userId, context.Tokens.HashRefreshToken("valid_token"), "device-1", null, null, false, false, Now);
        session.Revoke(SessionRevokeReason.AdminRevoked, Now);
        context.Sessions.Sessions.Add(session);

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", "device-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("session_superseded");
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_on_cache_miss_queries_database_resolves_effective_permissions_and_populates_perms_mask_cache()
    {
        var role = Role.Create("Manager", "manager", RoleScope.Staff, [PermissionCatalog.CourseRead, PermissionCatalog.RoleManage]);
        var user = User.RegisterVerified(Mail, "Manager User", Hash, Now);
        user.AssignRole(role.Id, null, Now);

        var sessionId = Guid.NewGuid();
        var (handler, context) = CreateHandler(user: user, roles: [role]);
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(user.Id, sessionId, Guid.NewGuid());

        var session = Session.Open(sessionId, user.Id, context.Tokens.HashRefreshToken("valid_token"), "device-1", null, null, false, false, Now);
        context.Sessions.Sessions.Add(session);

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", "device-1"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AccessToken.Should().NotBeNull();
        result.RefreshToken.Should().NotBeNull();
        result.SessionId.Should().Be(sessionId);

        // Permissions: CourseRead (bit 0) | RoleManage (bit 9)
        ulong expectedBitmask = (1UL << 0) | (1UL << 9);
        context.Tokens.LastCreatedBitmask.Should().Be(expectedBitmask);

        // Database was queried once on cache miss
        context.Users.FindByIdWithPermissionsCount.Should().Be(1);
        context.Roles.FindByIdsCount.Should().Be(1);

        // Bitmask must be cached under perms_mask:{userId}
        var cacheKey = $"perms_mask:{user.Id:D}";
        context.Cache.TryGetValue(cacheKey, out ulong cachedBitmask).Should().BeTrue();
        cachedBitmask.Should().Be(expectedBitmask);
    }

    [Fact]
    public async Task Handle_on_cache_hit_uses_cached_bitmask_and_avoids_database_queries()
    {
        var role = Role.Create("Student", "student", RoleScope.Student, [PermissionCatalog.CourseRead]);
        var user = User.RegisterVerified(Mail, "Student User", Hash, Now);
        user.AssignRole(role.Id, null, Now);

        var sessionId = Guid.NewGuid();
        var (handler, context) = CreateHandler(user: user, roles: [role]);
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(user.Id, sessionId, Guid.NewGuid());

        var session = Session.Open(sessionId, user.Id, context.Tokens.HashRefreshToken("valid_token"), "device-1", null, null, false, false, Now);
        context.Sessions.Sessions.Add(session);

        // Pre-populate cache (simulating prior login or previous refresh)
        ulong cachedBitmask = 1UL << 0;
        context.Cache.Set($"perms_mask:{user.Id:D}", cachedBitmask, TimeSpan.FromMinutes(10));

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", "device-1"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        context.Tokens.LastCreatedBitmask.Should().Be(cachedBitmask);

        // Zero queries to users and roles
        context.Users.FindByIdWithPermissionsCount.Should().Be(0);
        context.Roles.FindByIdsCount.Should().Be(0);
    }

    [Fact]
    public async Task Cache_entry_is_evicted_when_invalidation_token_is_triggered()
    {
        var role = Role.Create("Instructor", "instructor", RoleScope.Staff, [PermissionCatalog.CourseRead, PermissionCatalog.CourseWrite]);
        var user = User.RegisterVerified(Mail, "Instructor", Hash, Now);
        user.AssignRole(role.Id, null, Now);

        var sessionId1 = Guid.NewGuid();
        var (handler, context) = CreateHandler(user: user, roles: [role]);
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(user.Id, sessionId1, Guid.NewGuid());

        var session = Session.Open(sessionId1, user.Id, context.Tokens.HashRefreshToken("token_1"), "device-1", null, null, false, false, Now);
        context.Sessions.Sessions.Add(session);

        // First refresh: cache miss, resolves and caches
        await handler.Handle(new RefreshTokenCommand("token_1", "device-1"), CancellationToken.None);
        context.Users.FindByIdWithPermissionsCount.Should().Be(1);

        var cacheKey = $"perms_mask:{user.Id:D}";
        context.Cache.TryGetValue(cacheKey, out ulong _).Should().BeTrue();

        // RBAC invalidation occurs (e.g. admin modified permissions)
        context.Invalidation.Invalidate();

        context.Cache.TryGetValue(cacheKey, out ulong _).Should().BeFalse("Cache entry must be evicted upon invalidation");
    }

    [Fact]
    public async Task Handle_when_user_cannot_authenticate_fails_fast_and_does_not_issue_token()
    {
        var user = User.RegisterVerified(Mail, "Suspended User", Hash, Now);
        user.Suspend(); // CanAuthenticate == false

        var sessionId = Guid.NewGuid();
        var (handler, context) = CreateHandler(user: user);
        context.Tokens.ConfiguredClaims = new RefreshTokenClaims(user.Id, sessionId, Guid.NewGuid());

        var session = Session.Open(sessionId, user.Id, context.Tokens.HashRefreshToken("valid_token"), "device-1", null, null, false, false, Now);
        context.Sessions.Sessions.Add(session);

        var result = await handler.Handle(new RefreshTokenCommand("valid_token", "device-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("session_superseded");
        context.Tokens.LastCreatedBitmask.Should().BeNull();
    }

    private static (RefreshTokenCommandHandler Handler, TestContext Context) CreateHandler(
        User? user = null,
        Role[]? roles = null)
    {
        var context = new TestContext
        {
            Users = new TestUserRepository(user),
            Roles = new TestRoleRepository(roles ?? []),
            Sessions = new TestSessionRepository(),
            Tokens = new TestTokenService(),
            Clock = new TestClock(Now),
            SessionCache = new TestSessionCache(),
            Audit = new TestAuditRepo(),
            Cache = new MemoryCache(new MemoryCacheOptions()),
            Invalidation = new TestRbacInvalidationToken()
        };

        var handler = new RefreshTokenCommandHandler(
            context.Sessions,
            context.Tokens,
            context.Clock,
            context.SessionCache,
            context.Audit,
            context.Users,
            context.Roles,
            context.Cache,
            context.Invalidation);

        return (handler, context);
    }

    private sealed class TestContext
    {
        public TestUserRepository Users { get; init; } = null!;
        public TestRoleRepository Roles { get; init; } = null!;
        public TestSessionRepository Sessions { get; init; } = null!;
        public TestTokenService Tokens { get; init; } = null!;
        public TestClock Clock { get; init; } = null!;
        public TestSessionCache SessionCache { get; init; } = null!;
        public TestAuditRepo Audit { get; init; } = null!;
        public MemoryCache Cache { get; init; } = null!;
        public TestRbacInvalidationToken Invalidation { get; init; } = null!;
    }

    private sealed class TestTokenService : ITokenService
    {
        public bool ValidationResult { get; set; } = true;
        public RefreshTokenClaims? ConfiguredClaims { get; set; }
        public ulong? LastCreatedBitmask { get; private set; }
        public DateTimeOffset? LastCreatedNow { get; private set; }

        public string CreateAccessToken(Guid userId, Guid sessionId, ulong permissionsBitmask, DateTimeOffset now)
        {
            LastCreatedBitmask = permissionsBitmask;
            LastCreatedNow = now;
            return "mock_access_token";
        }

        public string CreateRefreshToken(Guid userId, Guid sessionId, DateTimeOffset now) => "mock_next_refresh_token";

        public bool TryValidateRefreshToken(string token, DateTimeOffset now, out RefreshTokenClaims claims)
        {
            if (!ValidationResult)
            {
                claims = default!;
                return false;
            }

            claims = ConfiguredClaims ?? new RefreshTokenClaims(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            return true;
        }

        public byte[] HashRefreshToken(string token) =>
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
    }

    private sealed class TestUserRepository(User? user = null) : IUserRepository
    {
        public int FindByIdWithPermissionsCount { get; private set; }

        public Task<User?> FindByIdWithPermissionsAsync(Guid id, bool track = false, CancellationToken ct = default)
        {
            FindByIdWithPermissionsCount++;
            return Task.FromResult(user?.Id == id ? user : null);
        }

        public Task<User?> FindByEmailAsync(Email email, bool track = false, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(user);
        public Task<User?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(user?.Id == id ? user : null);
        public Task<(bool HasSessionMulti, bool IsStudent)> GetAuthenticationPolicyAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult((false, false));
        public Task<IReadOnlyList<Guid>> FindIdsByRoleIdAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task AcquireLoginLockAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(Email email, CancellationToken ct = default) => Task.FromResult(user is not null);
        public Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(User user) { }
    }

    private sealed class TestRoleRepository(params Role[] roles) : IRoleRepository
    {
        public int FindByIdsCount { get; private set; }

        public Task<IReadOnlyList<Role>> FindByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            FindByIdsCount++;
            return Task.FromResult<IReadOnlyList<Role>>(roles.Where(r => ids.Contains(r.Id)).ToArray());
        }

        public Task<Role?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(roles.SingleOrDefault(r => r.Id == id));
        public Task<Role?> FindBySlugAsync(string slug, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(roles.SingleOrDefault(r => r.Slug == slug));
        public Task<IReadOnlyList<Role>> FindAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(roles);
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
        public readonly List<Session> UpdatedSessions = [];
        public int FindByIdForUpdateCount { get; private set; }

        public Task<Session?> FindByIdForUpdateAsync(Guid sessionId, CancellationToken ct = default)
        {
            FindByIdForUpdateCount++;
            return Task.FromResult(Sessions.SingleOrDefault(s => s.Id == sessionId));
        }

        public Task<Session?> FindByIdAsync(Guid sessionId, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.Id == sessionId));
        public Task<Session?> FindByRefreshHashAsync(byte[] hash, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.RefreshTokenHash.SequenceEqual(hash)));
        public Task<Session?> FindByRefreshHashForUpdateAsync(byte[] hash, CancellationToken ct = default) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.RefreshTokenHash.SequenceEqual(hash)));
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
        public void Update(Session session) => UpdatedSessions.Add(session);
        public Task<int> BulkRevokeByUserIdAsync(Guid userId, SessionRevokeReason reason, Guid? exceptSessionId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RevokeOldestNAsync(Guid userId, int count, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RevokeAllByUserIdAsync(Guid userId, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task SetEnforceSingleSessionAsync(Guid userId, bool enforce, DateTimeOffset now, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset now, DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class TestClock(DateTimeOffset initialNow) : IPlatformClock
    {
        public DateTimeOffset Now { get; set; } = initialNow;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class TestSessionCache : ISessionCacheHook
    {
        public readonly List<Guid> EvictedSessions = [];
        public void Evict(Guid sessionId) => EvictedSessions.Add(sessionId);
    }

    private sealed class TestAuditRepo : ISecurityEventRepository
    {
        public readonly List<(string EventName, Guid? UserId)> Events = [];
        public Task AddAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null, JsonDocument? details = null, DateTimeOffset? createdAt = null, CancellationToken ct = default)
        {
            Events.Add((eventName, userId));
            return Task.CompletedTask;
        }
        public Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
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
}
