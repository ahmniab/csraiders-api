using System.Globalization;
using System.Numerics;
using CsRaiders.Api.Domain.Constants;
using FluentAssertions;
using Xunit;

namespace CsRaiders.Api.Domain.UnitTests;

public sealed class PermissionBitmaskMapperTests
{
    [Fact]
    public void All_twenty_catalog_permissions_map_to_distinct_powers_of_two()
    {
        var bits = new HashSet<ulong>();
        for (var i = 0; i < PermissionCatalog.All.Count; i++)
        {
            var perm = PermissionCatalog.All[i];
            var bit = PermissionBitmaskMapper.GetBit(perm);

            bit.Should().Be(1UL << i, $"Permission {perm} must map to bit index {i}");
            bits.Add(bit).Should().BeTrue($"Bit {bit} for permission {perm} must be unique");
        }

        bits.Should().HaveCount(20);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown:permission")]
    [InlineData("course:delete")]
    [InlineData("admin:all")]
    public void GetBit_returns_zero_for_unrecognized_or_null_strings(string? input)
    {
        PermissionBitmaskMapper.GetBit(input).Should().Be(0UL);
    }

    [Fact]
    public void ToBitmask_aggregates_permissions_correctly()
    {
        var subset = new[] { PermissionCatalog.CourseRead, PermissionCatalog.UserWrite, PermissionCatalog.SessionRevoke };
        var expectedMask = (1UL << 0) | (1UL << 8) | (1UL << 19);

        var mask = PermissionBitmaskMapper.ToBitmask(subset);

        mask.Should().Be(expectedMask);
    }

    [Fact]
    public void ToBitmask_with_all_catalog_permissions_equals_full_catalog_bitmask()
    {
        const ulong expectedFullMask = (1UL << 20) - 1; // 1,048,575

        var mask = PermissionBitmaskMapper.ToBitmask(PermissionCatalog.All);

        mask.Should().Be(expectedFullMask);
        mask.Should().Be(1048575UL);
    }

    [Fact]
    public void ToBitmask_ignores_null_empty_and_unrecognized_strings_fail_closed()
    {
        var input = new[] { PermissionCatalog.CourseRead, null!, "", "nonexistent:perm", PermissionCatalog.SettingsRead };
        var expectedMask = (1UL << 0) | (1UL << 16);

        var mask = PermissionBitmaskMapper.ToBitmask(input);

        mask.Should().Be(expectedMask);
    }

    [Fact]
    public void ToBitmask_with_null_collection_returns_zero()
    {
        PermissionBitmaskMapper.ToBitmask(null).Should().Be(0UL);
    }

    [Fact]
    public void ToPermissionNames_recovers_exact_catalog_names_for_full_mask()
    {
        const ulong fullMask = (1UL << 20) - 1;

        var names = PermissionBitmaskMapper.ToPermissionNames(fullMask);

        names.Should().Equal(PermissionCatalog.All);
    }

    [Fact]
    public void ToPermissionNames_with_empty_or_zero_mask_returns_empty_collection()
    {
        PermissionBitmaskMapper.ToPermissionNames(0UL).Should().BeEmpty();
    }

    [Fact]
    public void ToPermissionNames_ignores_high_bits_above_catalog_range()
    {
        // Set bit 0 (CourseRead) plus higher unused bits: bit 20, bit 30, bit 63
        ulong maskWithHighBits = (1UL << 0) | (1UL << 20) | (1UL << 30) | (1UL << 63);

        var names = PermissionBitmaskMapper.ToPermissionNames(maskWithHighBits);

        names.Should().ContainSingle().Which.Should().Be(PermissionCatalog.CourseRead);
    }

    [Theory]
    [InlineData(PermissionCatalog.CourseRead, 1UL << 0)]
    [InlineData(PermissionCatalog.UserWrite, 1UL << 8)]
    [InlineData(PermissionCatalog.RoleManage, 1UL << 9)]
    [InlineData(PermissionCatalog.SessionRevoke, 1UL << 19)]
    public void HasPermission_evaluates_presence_accurately(string permission, ulong requiredBit)
    {
        ulong bitmask = (1UL << 0) | (1UL << 9); // CourseRead | RoleManage

        if (permission is PermissionCatalog.CourseRead or PermissionCatalog.RoleManage)
        {
            PermissionBitmaskMapper.HasPermission(bitmask, requiredBit).Should().BeTrue();
        }
        else
        {
            PermissionBitmaskMapper.HasPermission(bitmask, requiredBit).Should().BeFalse();
        }
    }

    [Fact]
    public void HasPermission_returns_false_for_zero_or_unknown_required_bit()
    {
        const ulong validMask = (1UL << 20) - 1;

        PermissionBitmaskMapper.HasPermission(validMask, 0UL).Should().BeFalse();
        PermissionBitmaskMapper.HasPermission(0UL, 0UL).Should().BeFalse();
        PermissionBitmaskMapper.HasPermission(0UL, 1UL << 0).Should().BeFalse();
    }

    [Fact]
    public void PermissionBits_enum_flags_match_mapper_bit_values()
    {
        ((ulong)PermissionBits.None).Should().Be(0UL);
        ((ulong)PermissionBits.CourseRead).Should().Be(1UL << 0);
        ((ulong)PermissionBits.SessionRevoke).Should().Be(1UL << 19);

        var flags = PermissionBits.CourseRead | PermissionBits.RoleManage;
        flags.HasFlag(PermissionBits.CourseRead).Should().BeTrue();
        flags.HasFlag(PermissionBits.RoleManage).Should().BeTrue();
        flags.HasFlag(PermissionBits.CourseWrite).Should().BeFalse();
    }

    [Theory]
    [InlineData(0UL)] // None
    [InlineData(1UL << 0)] // CourseRead only
    [InlineData(1UL << 19)] // SessionRevoke only
    [InlineData((1UL << 0) | (1UL << 8) | (1UL << 12) | (1UL << 19))] // Mixed subset
    [InlineData((1UL << 20) - 1)] // Full catalog mask (1048575)
    public void Client_BigInt_bitwise_evaluation_matches_server_ulong_for_all_catalog_permissions(ulong serverMask)
    {
        var permsString = serverMask.ToString(CultureInfo.InvariantCulture);

        for (var i = 0; i < PermissionCatalog.All.Count; i++)
        {
            var requiredServerBit = 1UL << i;
            var requiredClientBit = BigInteger.One << i;

            var serverAllowed = PermissionBitmaskMapper.HasPermission(serverMask, requiredServerBit);
            var clientAllowed = ClientHasPermission(permsString, requiredClientBit);

            clientAllowed.Should().Be(serverAllowed, $"Permission {PermissionCatalog.All[i]} (bit {i}) must match between server and client");
        }
    }

    [Fact]
    public void Client_BigInt_bitwise_evaluation_preserves_precision_for_high_bits_without_floating_point_truncation()
    {
        // In IEEE-754 double precision (JavaScript Number), integers > 2^53 - 1 lose precision in the least significant bits.
        // For instance, (1UL << 54) + 1UL cannot be accurately represented by a 64-bit float.
        const ulong testBitmask = (1UL << 54) | 1UL;
        var permsString = testBitmask.ToString(CultureInfo.InvariantCulture);

        // Prove floating point loss:
        var asDouble = (double)testBitmask;
        var doubleBitmask = (ulong)asDouble;
        (doubleBitmask == testBitmask).Should().BeFalse("Float conversion loses least significant bit precision");
        ((doubleBitmask & 1UL) != 0).Should().BeFalse("Float-based bitwise evaluation fails to detect bit 0");

        // Prove BigInt exact preservation:
        var clientMask = BigInteger.Parse(permsString, CultureInfo.InvariantCulture);
        var hasBit0 = (clientMask & (BigInteger.One << 0)) != BigInteger.Zero;
        var hasBit54 = (clientMask & (BigInteger.One << 54)) != BigInteger.Zero;
        var hasBit53 = (clientMask & (BigInteger.One << 53)) != BigInteger.Zero;

        hasBit0.Should().BeTrue("BigInt preserves bit 0");
        hasBit54.Should().BeTrue("BigInt preserves bit 54");
        hasBit53.Should().BeFalse("BigInt accurately reports unset bit 53");

        // Prove ulong.MaxValue boundary
        const ulong maxUlong = ulong.MaxValue;
        var maxPermsString = maxUlong.ToString(CultureInfo.InvariantCulture);
        var maxClientMask = BigInteger.Parse(maxPermsString, CultureInfo.InvariantCulture);

        for (var bit = 0; bit < 64; bit++)
        {
            var hasBit = (maxClientMask & (BigInteger.One << bit)) != BigInteger.Zero;
            hasBit.Should().BeTrue($"BigInt preserves bit {bit} of ulong.MaxValue");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("+100")]
    [InlineData("1.5")]
    [InlineData("0x10")]
    [InlineData("not-a-number")]
    public void Client_BigInt_fails_closed_on_invalid_claim_strings(string? invalidClaim)
    {
        var clientAllowed = ClientHasPermission(invalidClaim, BigInteger.One);
        clientAllowed.Should().BeFalse();
    }

    private static bool ClientHasPermission(string? permsClaim, BigInteger requiredBit)
    {
        if (string.IsNullOrWhiteSpace(permsClaim) || !permsClaim.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (!BigInteger.TryParse(permsClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var bitmask))
        {
            return false;
        }

        return (bitmask & requiredBit) != BigInteger.Zero;
    }
}
