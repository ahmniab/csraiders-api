# Controllers

## Overview
Exposes the REST API surface for the CsRaiders platform. Handles incoming HTTP requests, performs parameter and request validation, coordinates authentication and authorization checks, issues session and fingerprint cookies, and delegates domain workflows to mediator commands and queries.

## Interface
- `AdminRolesController` role and permission catalog management endpoints under `api/v1/admin/roles` (`[AdminRolesController.cs:AdminRolesController]`)
- `AdminUsersController` user role assignment, permission override, suspension, and reactivation endpoints under `api/v1/admin/users` (`[AdminUsersController.cs:AdminUsersController]`)
- `AuthController` registration, verification, login, refresh, logout, and password recovery endpoints under `api/v1/auth` (`[AuthController.cs:AuthController]`)
- `MeSessionsController` current user session inspection and revocation endpoints under `api/v1/me/sessions` (`[MeSessionsController.cs:MeSessionsController]`)
- DTO records for admin and auth requests: `CreateRoleRequest`, `UpdateRoleRequest`, `UpdateRolePermissionsRequest`, `SetUserPermissionOverrideRequest`, `ElevateUserRolesRequest`, `RegisterRequest`, `VerifyOtpRequest`, `ResendOtpRequest`, `LoginRequest`, `ForgotPasswordRequest`, `ResetPasswordRequest`, `ChangePasswordRequest` (`[AdminRolesController.cs:CreateRoleRequest]`, `[AdminUsersController.cs:SetUserPermissionOverrideRequest]`, `[AuthController.cs:RegisterRequest]`)

## Invariants
- Admin role management endpoints require the `role:manage` permission (`[AdminRolesController.cs:AdminRolesController]`).
- Role creation and mutation require a valid `RoleScope` string ("student" or "staff"), returning HTTP 422 if invalid (`[AdminRolesController.cs:AdminRolesController.Create]`, `[AdminRolesController.cs:AdminRolesController.Update]`).
- Admin user management endpoints require the `user:manage` permission (`[AdminUsersController.cs:AdminUsersController]`).
- Setting a user permission override requires either `Clear == true` or a valid effect ("grant" or "deny"), returning HTTP 422 otherwise (`[AdminUsersController.cs:AdminUsersController.SetPermission]`).
- Token refresh (`/api/v1/auth/refresh`) and logout (`/api/v1/auth/logout`) enforce `OriginAndHeaderValidationFilter` CSRF checks (`[AuthController.cs:AuthController.Refresh]`, `[AuthController.cs:AuthController.Logout]`).
- Logout, password change, and session endpoints enforce the `AuthenticatedOnly` policy requiring valid `sub` and `sid` claims (`[AuthController.cs:AuthController.Logout]`, `[AuthController.cs:AuthController.ChangePassword]`, `[MeSessionsController.cs:MeSessionsController]`).

## Side Effects
- Dispatches domain and application commands and queries via `IMediator.Send` (`[AdminRolesController.cs:AdminRolesController]`, `[AdminUsersController.cs:AdminUsersController]`, `[AuthController.cs:AuthController]`, `[MeSessionsController.cs:MeSessionsController]`).
- Reads roles directly from repository via `IRoleRepository.ListAsync` and `IRoleRepository.FindByIdAsync` (`[AdminRolesController.cs:AdminRolesController.List]`, `[AdminRolesController.cs:AdminRolesController.Get]`).
- Sets, refreshes, or removes secure HTTP-only cookies (`__Host-csraiders_rt`, `csraiders_dfp`) on the HTTP response (`[AuthController.cs:AuthController.Login]`, `[AuthController.cs:AuthController.Refresh]`, `[AuthController.cs:AuthController.Logout]`).
- Modifies HTTP response headers, including `Retry-After` on login rate limiting or failures (`[AuthController.cs:AuthController.Login]`).

## Verification
- Run: `dotnet test`
