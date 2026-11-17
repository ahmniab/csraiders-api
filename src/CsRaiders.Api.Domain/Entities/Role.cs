using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Entities;

public sealed record PermissionChange(IReadOnlyCollection<string> Added, IReadOnlyCollection<string> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    /// <summary>When true the handler must run session reconciliation (FR-62.1) in the same transaction.</summary>
    public bool TouchesSessionMulti =>
        Added.Contains(PermissionCatalog.SessionMulti) || Removed.Contains(PermissionCatalog.SessionMulti);
}

public sealed class Role : Entity
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public bool IsSystem { get; set; }
    public RoleScope Scope { get; set; }
    public List<string> Permissions { get; set; } = [];
}
