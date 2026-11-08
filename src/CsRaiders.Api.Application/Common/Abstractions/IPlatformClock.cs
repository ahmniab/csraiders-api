namespace CsRaiders.Api.Application.Common.Abstractions;

public interface IPlatformClock
{
    DateTimeOffset UtcNow { get; }
}
