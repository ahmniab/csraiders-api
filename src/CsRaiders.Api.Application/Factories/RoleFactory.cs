using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="Role"/>. Mutation lives in <see cref="Extensions.RoleExtensions"/>.</summary>
public static class RoleFactory
{
    public static Role Create(string name, string slug, RoleScope scope, IEnumerable<string> permissions, bool isSystem = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var role = new Role { Name = name.Trim(), Slug = slug.Trim().ToLowerInvariant(), Scope = scope, IsSystem = isSystem };
        role.SetPermissions(permissions);
        return role;
    }
}
