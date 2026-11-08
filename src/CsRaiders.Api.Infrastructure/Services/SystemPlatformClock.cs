using CsRaiders.Api.Application.Common.Abstractions;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class SystemPlatformClock : IPlatformClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
