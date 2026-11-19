using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.IntegrationTests;

public sealed class RefreshRotationTests
{
    [Fact]
    public void Rotation_has_a_fifteen_second_grace_and_reuse_is_irreversible()
    {
        var now = DateTimeOffset.UtcNow;
        var session = SessionFactory.Open(Guid.NewGuid(), Hash(1), "device", null, null, true, true, now);

        Assert.Equal(RefreshOutcome.Rotated, session.TryRotate(Hash(1), Hash(2), "device", now));
        Assert.Equal(RefreshOutcome.Rotated, session.TryRotate(Hash(1), Hash(3), "device", now.AddSeconds(15)));
        Assert.Equal(RefreshOutcome.ReuseDetected, session.TryRotate(Hash(2), Hash(4), "device", now.AddSeconds(31)));
        Assert.Equal(SessionRevokeReason.ReuseDetected, session.RevokeReason);
    }

    private static byte[] Hash(byte value) => Enumerable.Repeat(value, 32).ToArray();
}
