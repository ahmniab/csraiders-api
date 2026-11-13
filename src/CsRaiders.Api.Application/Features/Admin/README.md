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
- `[RbacCommands.cs:RbacCommandSupport]`: Helper resolving effective permissions, verifying grantor authorization, detecting sensitive permission loss, and executing immediate session revocation.
- `[RbacCommands.cs:UpdateRolePermissionsCommandHandler]`: Handler for updating role permissions, revoking sessions upon sensitive permission loss, and reconciling downgraded user sessions.
- `[RbacCommands.cs:CreateRoleCommandHandler]`: Handler for validating slug uniqueness and persisting new roles.
- `[RbacCommands.cs:UpdateRoleCommandHandler]`: Handler for updating non-system roles and revoking sessions upon sensitive permission loss.
- `[RbacCommands.cs:DeleteRoleCommandHandler]`: Handler for deleting unassigned, non-system roles and revoking sessions if sensitive permissions were held.
- `[RbacCommands.cs:SetUserPermissionOverrideCommandHandler]`: Handler applying permission overrides, revoking sessions upon sensitive permission loss, and triggering multi-session reconciliation if revoked.
- `[RbacCommands.cs:ElevateUserRolesCommandHandler]`: Handler reassigning roles, revoking sessions upon sensitive permission loss, and reconciling sessions upon multi-session loss.
- [Users](./Users/README.md): Administrative commands and handlers for user account suspension and reactivation.

## Invariants
- `[RbacCommands.cs:RbacCommandSupport.CanGrant]`: Grantor must hold all permissions being granted or updated.
- `[RbacCommands.cs:DeleteRoleCommandHandler]`: Cannot delete system roles or roles currently assigned to one or more users.
- `[RbacCommands.cs:UpdateRoleCommandHandler]`: Cannot edit system roles or reassign slugs to match another existing role.
- `[RbacCommands.cs:SetUserPermissionOverrideCommandHandler]`: Overridden permission must be registered in `PermissionCatalog`, and grantor must possess granted permissions.
- `[RbacCommands.cs:ElevateUserRolesCommandHandler]`: Grantor must hold all permissions contained within the target assigned roles.
- `[RbacCommands.cs:RbacCommandSupport]`: Any de-escalation or loss of sensitive permissions (`role:manage`, `payment:refund`, `user:write`, `session:revoke`) immediately revokes all active sessions for affected users.

## Side Effects
- `[RbacCommands.cs:UpdateRolePermissionsCommandHandler]`: Persists updated role permissions, revokes all active sessions for users losing sensitive permissions, triggers session reconciliation upon `session:multi` loss, invalidates RBAC cache, and writes `role_permissions_updated` audit log.
- `[RbacCommands.cs:CreateRoleCommandHandler]`: Inserts new role record into database, invalidates RBAC cache, and writes `role_created` audit log.
- `[RbacCommands.cs:UpdateRoleCommandHandler]`: Updates role details in database, revokes sessions for users losing sensitive permissions, invalidates RBAC cache, and writes `role_updated` audit log.
- `[RbacCommands.cs:DeleteRoleCommandHandler]`: Deletes role record from database, revokes sessions for users if sensitive permissions were held, invalidates RBAC cache, and writes `role_deleted` audit log.
- `[RbacCommands.cs:SetUserPermissionOverrideCommandHandler]`: Updates user permission override records, revokes sessions if sensitive permissions are lost, triggers session reconciliation if `session:multi` is lost, invalidates RBAC cache, and writes `user_permission_override_updated` audit log.
- `[RbacCommands.cs:ElevateUserRolesCommandHandler]`: Reassigns user role mappings in database, revokes sessions if sensitive permissions are lost, triggers session reconciliation if `session:multi` is lost, invalidates RBAC cache, and writes `user_roles_updated` audit log.

## Verification
- Run: `dotnet test`
