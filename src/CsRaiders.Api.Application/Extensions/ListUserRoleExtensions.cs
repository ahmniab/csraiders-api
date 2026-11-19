using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Extensions;

public static class ListUserRoleExtensions
{
    public static bool CanHaveMultiSessions(this List<UserRole> userRoles)
    {
        foreach (var ur in userRoles)
        {
            if (ur.Role == null)
             continue;
            foreach (var p in ur.Role.Permissions)
                if (p == PermissionCatalog.SessionMulti)
                    return true;
        }
        return false;
    }
    
}