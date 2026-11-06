namespace CsRaiders.Api.Domain.Common;

public static class IdGenerator
{
    public static Guid New()
    {
#if NET9_0_OR_GREATER
        return Guid.CreateVersion7();   // time-ordered: better index locality
#else
        return Guid.NewGuid();
#endif
    }
}
