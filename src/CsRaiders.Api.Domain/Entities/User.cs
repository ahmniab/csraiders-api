using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Domain.Entities;

public sealed class User : Entity
{
    public Email Email { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? AcademicYear { get; set; }
    public string PasswordHash { get; set; } = null!;
    public short PepperVersion { get; set; } = 1;
    public UserStatus Status { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public List<UserRole> Roles { get; set; } = [];
    public List<UserPermission> PermissionOverrides { get; set; } = [];
}
