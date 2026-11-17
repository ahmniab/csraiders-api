namespace CsRaiders.Api.Domain.Common;

public abstract class Entity
{
    protected Entity() { }

    /// <summary>Creates an entity with a caller-supplied identity (e.g. session id embedded in the refresh token).</summary>
    protected Entity(Guid id) => Id = id;

    public Guid Id { get; protected set; } = IdGenerator.New();

    /// <summary>PostgreSQL xmin, used as the optimistic-concurrency token (spec 7.2). Mapped in Infrastructure.</summary>
    public uint Version { get; private set; }
}
