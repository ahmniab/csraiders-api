using System.Net;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Application.Features.Admin;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;
using CsRaiders.Api.Domain.Policies;
using CsRaiders.Api.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;

namespace CsRaiders.Api.Application.UnitTests.Admin;

public sealed class RbacGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private const string Hash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn";

    [Fact]
    public void Escalation_guard_allows_only_permissions_already_effective_for_grantor()
    {
        var held = new HashSet<string>([PermissionCatalog.RoleManage, PermissionCatalog.CourseRead]);
        RbacCommandSupport.CanGrant(held, [PermissionCatalog.CourseRead]).Should().BeTrue();
        RbacCommandSupport.CanGrant(held, [PermissionCatalog.UserWrite]).Should().BeFalse();
    }

    [Fact]
    public void Deny_override_never_permits_a_role_grant()
    {
        var role = RoleFactory.Create("Staff", "staff", RoleScope.Staff, [PermissionCatalog.SessionMulti]);
        var user = UserFactory.RegisterVerified(Email.Create("student@example.com"), "Student", Hash, Now);
        user.SetPermissionOverride(PermissionCatalog.SessionMulti, PermissionEffect.Deny, Guid.NewGuid(), Now);
        EffectivePermissions.Resolve([role], user.PermissionOverrides).Should().NotContain(PermissionCatalog.SessionMulti);
    }

    [Fact]
    public void Student_scope_role_rejects_staff_permissions()
    {
        var action = () => RoleFactory.Create("Student", "student", RoleScope.Student, [PermissionCatalog.UserWrite]);
        action.Should().Throw<DomainException>().Which.Code.Should().Be("student_role_forbidden_permission");
    }

    [Fact]
    public async Task Successful_role_mutation_invalidates_the_global_permission_token()
    {
        var grantorRole = RoleFactory.Create("Manager", "manager", RoleScope.Staff, [PermissionCatalog.RoleManage, PermissionCatalog.CourseRead]);
        var targetRole = RoleFactory.Create("Tutor", "tutor", RoleScope.Staff, [PermissionCatalog.CourseRead]);
        var grantor = UserFactory.RegisterVerified(Email.Create("manager@example.com"), "Manager", Hash, Now);
        grantor.AssignRole(grantorRole.Id, null, Now);
        var users = new Users(grantor);
        var roles = new Roles(grantorRole, targetRole);
        var token = new Token();
        var handler = new UpdateRolePermissionsCommandHandler(users, roles, new Sessions(), new Cache(), new Reconciler(), token, new Clock(), new Audit());

        var result = await handler.Handle(new UpdateRolePermissionsCommand(grantor.Id, targetRole.Id, [PermissionCatalog.CourseRead]), default);

        result.Succeeded.Should().BeTrue();
        token.Invalidated.Should().BeTrue();
    }

    private sealed class Token : IRbacInvalidationToken
    {
        public bool Invalidated { get; private set; }
        public CancellationToken CurrentToken => CancellationToken.None;
        public void Invalidate() => Invalidated = true;
    }

    private sealed class Reconciler : ISessionReconciliationService
    {
        public Task ReconcileAfterMultiSessionLossAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Cache : IDistributedCache
    {
        public byte[]? Get(string key) => null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Task.CompletedTask;

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) { }

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class Clock : IPlatformClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Sessions : ISessionRepository
    {
        public Task<Session?> FindByIdAsync(Guid sessionId, bool track = false, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<Session?> FindByRefreshHashAsync(byte[] hash, bool track = false, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<Session?> FindByRefreshHashForUpdateAsync(byte[] hash, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<Session?> FindByIdForUpdateAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<IReadOnlyList<Session>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, bool track = false, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Session>>([]);
        public Task<int> CountActiveByUserIdAsync(Guid userId, DateTimeOffset? now = null, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<CsRaiders.Api.Application.Common.Models.SessionSummary>> ListByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CsRaiders.Api.Application.Common.Models.SessionSummary>>([]);
        public Task AddAsync(Session session, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(Session session) { }
        public Task<int> BulkRevokeByUserIdAsync(Guid userId, SessionRevokeReason reason, Guid? exceptSessionId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RevokeOldestNAsync(Guid userId, int count, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RevokeAllByUserIdAsync(Guid userId, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task SetEnforceSingleSessionAsync(Guid userId, bool enforce, DateTimeOffset now, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteStaleAsync(DateTimeOffset now, DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class Audit : ISecurityAuditWriter
    {
        public Task WriteAsync(string eventName, Guid? userId = null, Email? email = null, IPAddress? ip = null, object? details = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Users(User grantor) : IUserRepository
    {
        public Task<User?> FindByEmailAsync(Email email, bool track = false, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) => Task.FromResult<User?>(id == grantor.Id ? grantor : null);
        public Task<User?> FindByIdWithPermissionsAsync(Guid id, bool track = false, CancellationToken ct = default) => Task.FromResult<User?>(id == grantor.Id ? grantor : null);
        public Task<User?> FindByEmailWithPermissionsAsync(Email email, bool track = false, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<(bool HasSessionMulti, bool IsStudent)> GetAuthenticationPolicyAsync(Guid userId, CancellationToken ct = default) => Task.FromResult((false, false));
        public Task<IReadOnlyList<Guid>> FindIdsByRoleIdAsync(Guid roleId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task AcquireLoginLockAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(Email email, CancellationToken ct = default) => Task.FromResult(false);
        public Task<CsRaiders.Api.Application.Common.Models.PagedResult<CsRaiders.Api.Application.Common.Models.UserSummary>> ListAsync(CsRaiders.Api.Application.Common.Models.UserFilter filter, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(User user) { }
    }

    private sealed class Roles(params Role[] values) : IRoleRepository
    {
        public Task<Role?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default) => Task.FromResult<Role?>(values.SingleOrDefault(x => x.Id == id));
        public Task<Role?> FindBySlugAsync(string slug, bool track = false, CancellationToken ct = default) => Task.FromResult<Role?>(values.SingleOrDefault(x => x.Slug == slug));
        public Task<IReadOnlyList<Role>> FindAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Role>>(values);
        public Task<IReadOnlyList<Role>> FindByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Role>>(values.Where(x => ids.Contains(x.Id)).ToArray());
        public Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Role>>(values);
        public Task<int> CountUsersAsync(Guid roleId, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddAsync(Role role, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(Role role) { }
        public void Delete(Role role) { }
    }
}
