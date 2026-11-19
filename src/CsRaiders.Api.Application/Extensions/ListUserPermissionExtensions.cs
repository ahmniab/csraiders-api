using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Extensions;

public static class ListUserPermissionExtensions
{
    public static bool? CanHaveMultiSessions(this List<UserPermission> permissions)
    {
        foreach (var p in permissions)
            if (p.Permission == PermissionCatalog.SessionMulti)
                return p.Effect == PermissionEffect.Grant;
        
        return null;
    }
}