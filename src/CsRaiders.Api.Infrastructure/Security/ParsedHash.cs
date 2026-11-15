namespace CsRaiders.Api.Infrastructure.Security;
public sealed record ParsedHash(
    byte[] Salt,
    byte[] Hash,
    int Pepper,
    HashParams Params
);

public sealed record HashParams(
    int MemoryKb,
    int Iterations,
    int Parallelism
);