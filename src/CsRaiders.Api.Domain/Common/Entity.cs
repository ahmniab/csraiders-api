namespace CsRaiders.Api.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = IdGenerator.New();

    /// <summary>PostgreSQL xmin, used as the optimistic-concurrency token (spec 7.2). Mapped in Infrastructure.</summary>
    public uint Version { get; private set; }
}
