# Admin

## Overview
Encapsulates administrative operations and commands for Role-Based Access Control (RBAC), including role management, user permission overrides, role elevation, and user lifecycle administration.

## Interface
- `[RbacCommands.cs:UpdateRolePermissionsCommand]`: Mediator command to modify permissions assigned to an existing role.
- `[RbacCommands.cs:SetUserPermissionOverrideCommand]`: Mediator command to set or clear user-level granular permission overrides (`Grant`, `Deny`, or null).
- `[RbacCommands.cs:ElevateUserRolesCommand]`: Mediator command to reassign roles assigned to a target user.
- `[RbacCommands.cs:CreateRoleCommand]`: Mediator command to create a new custom role with assigned permissions.
- `[RbacCommands.cs:UpdateRoleCommand]`: Mediator command to update role metadata and permission sets.
- `[RbacCommands.cs:DeleteRoleCommand]`: Mediator command to remove a custom role.
- `[RbacCommands.cs:AdminChangeResult]`: Result record conveying change outcome, `NotFound`, and `Forbidden` states.
- `[RbacCommands.cs:RoleChangeResult]`: Result record conveying role creation outcome, generated `RoleId`, `Conflict`, and `Forbidden` states.
- `[RbacCommands.cs:RbacCommandSupport]`: Helper resolving effective permissions and verifying grantor authorization.
- `[RbacCommands.cs:UpdateRolePermissionsCommandHandler]`: Handler for updating role permissions and reconciling downgraded user sessions.
- `[RbacCommands.cs:CreateRoleCommandHandler]`: Handler for validating slug uniqueness and persisting new roles.
- `[RbacCommands.cs:UpdateRoleCommandHandler]`: Handler for updating non-system roles.
- `[RbacCommands.cs:DeleteRoleCommandHandler]`: Handler for deleting unassigned, non-system roles.
- `[RbacCommands.cs:SetUserPermissionOverrideCommandHandler]`: Handler applying permission overrides and triggering multi-session reconciliation if revoked.
- `[RbacCommands.cs:ElevateUserRolesCommandHandler]`: Handler reassigning roles and reconciling sessions upon multi-session loss.
- [Users](./Users/README.md): Administrative commands and handlers for user account suspension and reactivation.

## Invariants
- `[RbacCommands.cs:RbacCommandSupport.CanGrant]`: Grantor must hold all permissions being granted or updated.
- `[RbacCommands.cs:DeleteRoleCommandHandler]`: Cannot delete system roles or roles currently assigned to one or more users.
- `[RbacCommands.cs:UpdateRoleCommandHandler]`: Cannot edit system roles or reassign slugs to match another existing role.
- `[RbacCommands.cs:SetUserPermissionOverrideCommandHandler]`: Overridden permission must be registered in `PermissionCatalog`, and grantor must possess granted permissions.
- `[RbacCommands.cs:ElevateUserRolesCommandHandler]`: Grantor must hold all permissions contained within the target assigned roles.

## Side Effects
- `[RbacCommands.cs:UpdateRolePermissionsCommandHandler]`: Persists updated role permissions, triggers session reconciliation upon `session:multi` loss, invalidates RBAC cache, and writes `role_permissions_updated` audit log.
- `[RbacCommands.cs:CreateRoleCommandHandler]`: Inserts new role record into database, invalidates RBAC cache, and writes `role_created` audit log.
- `[RbacCommands.cs:UpdateRoleCommandHandler]`: Updates role details in database, invalidates RBAC cache, and writes `role_updated` audit log.
- `[RbacCommands.cs:DeleteRoleCommandHandler]`: Deletes role record from database, invalidates RBAC cache, and writes `role_deleted` audit log.
- `[RbacCommands.cs:SetUserPermissionOverrideCommandHandler]`: Updates user permission override records, triggers session reconciliation if `session:multi` is lost, invalidates RBAC cache, and writes `user_permission_override_updated` audit log.
- `[RbacCommands.cs:ElevateUserRolesCommandHandler]`: Reassigns user role mappings in database, triggers session reconciliation if `session:multi` is lost, invalidates RBAC cache, and writes `user_roles_updated` audit log.

## Verification
- Run: `dotnet test`
