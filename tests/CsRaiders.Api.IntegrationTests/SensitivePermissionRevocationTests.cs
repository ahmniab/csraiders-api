using System.Net;
using System.Security.Claims;
using CsRaiders.Api.API.Middleware;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Application.Features.Admin;
using CsRaiders.Api.Application.Features.Sessions.Queries;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace CsRaiders.Api.IntegrationTests;

public sealed class SensitivePermissionRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private const string Hash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn";

    [Theory]
    [InlineData(PermissionCatalog.RoleManage)]
    [InlineData(PermissionCatalog.PaymentManage)]
    [InlineData(PermissionCatalog.UserWrite)]
    [InlineData(PermissionCatalog.SessionRevoke)]
    public async Task UpdateRolePermissions_revokes_active_sessions_when_sensitive_permission_is_removed(string sensitivePerm)
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var targetRole = RoleFactory.Create("Operator", "operator", RoleScope.Staff, [PermissionCatalog.CourseRead, sensitivePerm]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(targetRole.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        users.MapUserToRole(targetUser.Id, targetRole.Id);
        var roles = new InMemoryRoleRepo(grantorRole, targetRole);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new UpdateRolePermissionsCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        // Remove the sensitive permission from the target role
        var command = new UpdateRolePermissionsCommand(grantor.Id, targetRole.Id, [PermissionCatalog.CourseRead]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.Equal(SessionRevokeReason.AdminRevoked, sessions.RevokedReasons[targetUser.Id]);
        Assert.True(cache.WasEvicted(activeSession.Id));
        Assert.True(token.Invalidated);
    }

    [Fact]
    public async Task UpdateRolePermissions_preserves_active_sessions_when_only_non_sensitive_permission_is_removed()
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var targetRole = RoleFactory.Create("Editor", "editor", RoleScope.Staff, [PermissionCatalog.CourseRead, PermissionCatalog.CourseWrite]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(targetRole.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        users.MapUserToRole(targetUser.Id, targetRole.Id);
        var roles = new InMemoryRoleRepo(grantorRole, targetRole);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new UpdateRolePermissionsCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        // Remove course:write (non-sensitive)
        var command = new UpdateRolePermissionsCommand(grantor.Id, targetRole.Id, [PermissionCatalog.CourseRead]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.False(cache.WasEvicted(activeSession.Id));
    }

    [Fact]
    public async Task UpdateRolePermissions_preserves_sessions_if_user_retains_sensitive_capability_via_another_role()
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var roleA = RoleFactory.Create("RoleA", "role-a", RoleScope.Staff, [PermissionCatalog.RoleManage]);
        var roleB = RoleFactory.Create("RoleB", "role-b", RoleScope.Staff, [PermissionCatalog.RoleManage, PermissionCatalog.CourseRead]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(roleA.Id, null, Now);
        targetUser.AssignRole(roleB.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        users.MapUserToRole(targetUser.Id, roleA.Id);
        users.MapUserToRole(targetUser.Id, roleB.Id);
        var roles = new InMemoryRoleRepo(grantorRole, roleA, roleB);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new UpdateRolePermissionsCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        // Remove role:manage from RoleA, but targetUser still has RoleB with role:manage
        var command = new UpdateRolePermissionsCommand(grantor.Id, roleA.Id, []);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.False(cache.WasEvicted(activeSession.Id));
    }

    [Theory]
    [InlineData(PermissionCatalog.RoleManage)]
    [InlineData(PermissionCatalog.PaymentManage)]
    [InlineData(PermissionCatalog.UserWrite)]
    [InlineData(PermissionCatalog.SessionRevoke)]
    public async Task SetUserPermissionOverride_revokes_sessions_when_sensitive_permission_is_denied(string sensitivePerm)
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var role = RoleFactory.Create("StaffRole", "staff-role", RoleScope.Staff, [sensitivePerm, PermissionCatalog.CourseRead]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(role.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        var roles = new InMemoryRoleRepo(grantorRole, role);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new SetUserPermissionOverrideCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        var command = new SetUserPermissionOverrideCommand(grantor.Id, targetUser.Id, sensitivePerm, PermissionEffect.Deny);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.Equal(SessionRevokeReason.AdminRevoked, sessions.RevokedReasons[targetUser.Id]);
        Assert.True(cache.WasEvicted(activeSession.Id));
    }

    [Fact]
    public async Task SetUserPermissionOverride_preserves_sessions_when_non_sensitive_permission_is_denied()
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var role = RoleFactory.Create("StaffRole", "staff-role", RoleScope.Staff, [PermissionCatalog.CourseRead]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(role.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        var roles = new InMemoryRoleRepo(grantorRole, role);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new SetUserPermissionOverrideCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        var command = new SetUserPermissionOverrideCommand(grantor.Id, targetUser.Id, PermissionCatalog.CourseRead, PermissionEffect.Deny);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.False(cache.WasEvicted(activeSession.Id));
    }

    [Fact]
    public async Task ElevateUserRoles_revokes_sessions_when_sensitive_capability_is_de_escalated()
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var adminRole = RoleFactory.Create("Admin", "admin", RoleScope.Staff, [PermissionCatalog.RoleManage, PermissionCatalog.CourseRead]);
        var tutorRole = RoleFactory.Create("Tutor", "tutor", RoleScope.Staff, [PermissionCatalog.CourseRead]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(adminRole.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        var roles = new InMemoryRoleRepo(grantorRole, adminRole, tutorRole);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new ElevateUserRolesCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        // De-escalate targetUser from Admin to Tutor
        var command = new ElevateUserRolesCommand(grantor.Id, targetUser.Id, [tutorRole.Id]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.Equal(SessionRevokeReason.AdminRevoked, sessions.RevokedReasons[targetUser.Id]);
        Assert.True(cache.WasEvicted(activeSession.Id));
    }

    [Fact]
    public async Task ElevateUserRoles_preserves_sessions_when_only_non_sensitive_roles_change()
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var readerRole = RoleFactory.Create("Reader", "reader", RoleScope.Staff, [PermissionCatalog.CourseRead]);
        var writerRole = RoleFactory.Create("Writer", "writer", RoleScope.Staff, [PermissionCatalog.CourseWrite]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(readerRole.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        var roles = new InMemoryRoleRepo(grantorRole, readerRole, writerRole);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new ElevateUserRolesCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        var command = new ElevateUserRolesCommand(grantor.Id, targetUser.Id, [writerRole.Id]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.False(cache.WasEvicted(activeSession.Id));
    }

    [Fact]
    public async Task UpdateRole_revokes_sessions_when_role_loses_sensitive_capability()
    {
        var grantorRole = RoleFactory.Create("SuperAdmin", "superadmin", RoleScope.Staff, PermissionCatalog.All);
        var targetRole = RoleFactory.Create("Manager", "manager", RoleScope.Staff, [PermissionCatalog.PaymentManage, PermissionCatalog.CourseRead]);

        var grantor = UserFactory.RegisterVerified(Email.Create("grantor@example.com"), "Grantor", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);

        var targetUser = UserFactory.RegisterVerified(Email.Create("target@example.com"), "Target User", Hash, Now);
        targetUser.AssignRole(targetRole.Id, null, Now);

        var activeSession = SessionFactory.Open(targetUser.Id, new byte[32], "test-device", IPAddress.Loopback, null, false, false, Now);

        var users = new InMemoryUserRepo(grantor, targetUser);
        users.MapUserToRole(targetUser.Id, targetRole.Id);
        var roles = new InMemoryRoleRepo(grantorRole, targetRole);
        var sessions = new InMemorySessionRepo(activeSession);
        var cache = new RecordingDistributedCache();
        var token = new TestRbacInvalidationToken();
        var clock = new TestClock(Now);
        var audit = new TestAudit();

        var handler = new UpdateRoleCommandHandler(
            users, roles, sessions, cache, new TestReconciler(), token, clock, audit);

        var command = new UpdateRoleCommand(grantor.Id, targetRole.Id, "Manager Updated", "manager", RoleScope.Staff, [PermissionCatalog.CourseRead]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(sessions.RevokedReasons.ContainsKey(targetUser.Id));
        Assert.Equal(SessionRevokeReason.AdminRevoked, sessions.RevokedReasons[targetUser.Id]);
        Assert.True(cache.WasEvicted(activeSession.Id));
    }

    [Fact]
    public async Task SessionValidationMiddleware_allows_an_active_session()
    {
        var sessionId = Guid.NewGuid();
        var status = new SessionStatus
        {
            SessionId = sessionId,
            UserId = Guid.NewGuid(),
            ExpiresAt = Now.AddMinutes(30),
            IdleExpiresAt = Now.AddMinutes(15)
        };

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(sessionId);

        await middleware.InvokeAsync(context, new FakeMediator(_ => status), new TestClock(Now));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task SessionValidationMiddleware_rejects_a_revoked_session_with_a_problem_response()
    {
        var sessionId = Guid.NewGuid();
        var status = new SessionStatus
        {
            SessionId = sessionId,
            UserId = Guid.NewGuid(),
            ExpiresAt = Now.AddMinutes(30),
            IdleExpiresAt = Now.AddMinutes(15),
            RevokedAt = Now.AddMinutes(-1)
        };

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(sessionId);

        await middleware.InvokeAsync(context, new FakeMediator(_ => status), new TestClock(Now));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("session_superseded", body);
    }

    [Fact]
    public async Task SessionValidationMiddleware_passes_through_when_the_session_claim_is_missing()
    {
        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", Guid.NewGuid().ToString())], "TestAuth"))
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, new FakeMediator(_ => throw new InvalidOperationException("should not read session")), new TestClock(Now));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Session_status_query_caches_reads_and_reflects_revocation_after_cache_eviction()
    {
        var session = SessionFactory.Open(Guid.NewGuid(), new byte[32], "device", IPAddress.Loopback, null, false, false, Now);
        var sessions = new InMemorySessionRepo(session);
        var cache = new RecordingDistributedCache();
        var handler = new GetSessionStatusByIdQueryHandler(cache, sessions);
        var query = new GetSessionStatusByIdQuery(session.Id.ToString());

        var first = await handler.Handle(query, CancellationToken.None);
        Assert.Null(first.RevokedAt);
        Assert.Contains($"sid:{session.Id}", cache.Store.Keys);

        session.Revoke(SessionRevokeReason.AdminRevoked, Now);
        await cache.RemoveAsync($"sid:{session.Id}");

        var second = await handler.Handle(query, CancellationToken.None);
        Assert.Equal(Now, second.RevokedAt);
        Assert.False(second.IsActive(Now));
    }

    private static DefaultHttpContext CreateHttpContext(Guid sessionId)
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("sid", sessionId.ToString())
        ], "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class FakeMediator(Func<object, SessionStatus> readStatus) : IMediator
    {
        public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => ValueTask.FromResult((TResponse)(object)readStatus(request));

        public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
            => ValueTask.FromResult((TResponse)(object)readStatus(command));

        public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
            => ValueTask.FromResult((TResponse)(object)readStatus(query));

        public ValueTask<object?> Send(object message, CancellationToken ct = default)
            => ValueTask.FromResult<object?>(readStatus(message));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object> CreateStream(object message, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification => ValueTask.CompletedTask;

        public ValueTask Publish(object notification, CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    private sealed class TestClock(DateTimeOffset now) : IPlatformClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class TestRbacInvalidationToken : IRbacInvalidationToken
    {
        public bool Invalidated { get; private set; }
        public CancellationToken CurrentToken => CancellationToken.None;
        public void Invalidate() => Invalidated = true;
    }

    private sealed class TestReconciler : ISessionReconciliationService
    {
        public Task ReconcileAfterMultiSessionLossAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestAudit : ISecurityAuditWriter
    {
        public Task WriteAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null, object? details = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        public Dictionary<string, byte[]> Store { get; } = [];
        public List<string> RemovedKeys { get; } = [];

        public bool WasEvicted(Guid sessionId) => RemovedKeys.Contains($"sid:{sessionId}");

        public byte[]? Get(string key) => Store.TryGetValue(key, out var value) ? value : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key)
        {
            RemovedKeys.Add(key);
            Store.Remove(key);
        }
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySessionRepo(params Session[] initial) : ISessionRepository
    {
        private readonly List<Session> _sessions = [.. initial];
        public Dictionary<Guid, SessionRevokeReason> RevokedReasons { get; } = [];

        public Task<Session?> FindByIdAsync(Guid sessionId, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(_sessions.SingleOrDefault(s => s.Id == sessionId));
        public Task<Session?> FindByRefreshHashAsync(byte[] hash, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(_sessions.SingleOrDefault(s => s.RefreshTokenHash.SequenceEqual(hash)));
        public Task<Session?> FindByRefreshHashForUpdateAsync(byte[] hash, CancellationToken ct = default) =>
            Task.FromResult(_sessions.SingleOrDefault(s => s.RefreshTokenHash.SequenceEqual(hash)));
        public Task<Session?> FindByIdForUpdateAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(_sessions.SingleOrDefault(s => s.Id == sessionId));
        public Task<IReadOnlyList<Session>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, bool track = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Session>>(_sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToArray());
        public Task<int> CountActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, CancellationToken ct = default) =>
            Task.FromResult(_sessions.Count(s => s.UserId == userId && s.RevokedAt == null));
        public Task<IReadOnlyList<SessionSummary>> ListByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionSummary>>([]);
        public Task AddAsync(Session session, CancellationToken ct = default)
        {
            _sessions.Add(session);
            return Task.CompletedTask;
        }
        public void Update(Session session) { }
        public Task<int> BulkRevokeByUserIdAsync(Guid userId, SessionRevokeReason reason, Guid? exceptSessionId, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task<int> RevokeOldestNAsync(Guid userId, int count, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task<int> RevokeAllByUserIdAsync(Guid userId, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default)
        {
            RevokedReasons[userId] = reason;
            var active = _sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToList();
            foreach (var s in active)
            {
                s.Revoke(reason, now);
            }
            return Task.FromResult(active.Count);
        }
        public Task SetEnforceSingleSessionAsync(Guid userId, bool enforce, DateTimeOffset now, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset now, DateTimeOffset cutoff, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class InMemoryUserRepo : IUserRepository
    {
        private readonly List<User> _users;
        private readonly Dictionary<Guid, List<Guid>> _roleToUsers = [];

        public InMemoryUserRepo(params User[] users)
        {
            _users = [.. users];
        }

        public void MapUserToRole(Guid userId, Guid roleId)
        {
            if (!_roleToUsers.TryGetValue(roleId, out var list))
            {
                list = [];
                _roleToUsers[roleId] = list;
            }
            if (!list.Contains(userId)) list.Add(userId);
        }

        public Task<User?> FindByEmailAsync(Email email, bool track = false, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_users.SingleOrDefault(u => u.Email == email));
        public Task<User?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(_users.SingleOrDefault(u => u.Id == id));
        public Task<User?> FindByIdWithPermissionsAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(_users.SingleOrDefault(u => u.Id == id));
        public Task<User?> FindByEmailWithPermissionsAsync(Email email, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(_users.SingleOrDefault(u => u.Email == email));
        public Task<(bool HasSessionMulti, bool IsStudent)> GetAuthenticationPolicyAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult((false, false));
        public Task<IReadOnlyList<Guid>> FindIdsByRoleIdAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(_roleToUsers.TryGetValue(roleId, out var list) ? list : []);
        public Task AcquireLoginLockAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(Email email, CancellationToken ct = default) =>
            Task.FromResult(_users.Any(u => u.Email == email));
        public Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(User user) { }
    }

    private sealed class InMemoryRoleRepo(params Role[] roles) : IRoleRepository
    {
        private readonly List<Role> _roles = [.. roles];

        public Task<Role?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(_roles.SingleOrDefault(r => r.Id == id));
        public Task<Role?> FindBySlugAsync(string slug, bool track = false, CancellationToken ct = default) =>
            Task.FromResult(_roles.SingleOrDefault(r => r.Slug == slug));
        public Task<IReadOnlyList<Role>> FindAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(_roles);
        public Task<IReadOnlyList<Role>> FindByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(_roles.Where(r => ids.Contains(r.Id)).ToArray());
        public Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(_roles);
        public Task<int> CountUsersAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task AddAsync(Role role, CancellationToken ct = default)
        {
            _roles.Add(role);
            return Task.CompletedTask;
        }
        public void Update(Role role) { }
        public void Delete(Role role) => _roles.Remove(role);
    }
}
