using System.Net;
using System.Threading.RateLimiting;
using CsRaiders.Api.API.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CsRaiders.Api.IntegrationTests;

public sealed class AuthRateLimitingTests
{
    private static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Development";
        builder.Configuration["ConnectionStrings:Default"] = "Host=localhost;Database=test;Username=test;Password=test";
        builder.Configuration["Auth:PasswordPepper:0"] = new string('p', 32);
        builder.Configuration["Auth:OtpHmacKey"] = new string('o', 32);
        builder.Configuration["Auth:JwtSigningKey"] = new string('k', 32);
        builder.Configuration["Site:Origin"] = "https://localhost";

        builder.AddCsRaidersApi();
        var app = builder.Build();

        var options = app.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        return options.GlobalLimiter!;
    }

    [Fact]
    public void Refresh_endpoint_allows_100_requests_per_minute_and_throttles_101st()
    {
        var limiter = CreateGlobalLimiter();
        var ip = IPAddress.Parse("10.0.0.1");

        for (var i = 0; i < 100; i++)
        {
            var context = CreateContext("/api/v1/auth/refresh", ip);
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired, $"Request {i + 1} should be permitted");
        }

        var throttledContext = CreateContext("/api/v1/auth/refresh", ip);
        using var throttledLease = limiter.AttemptAcquire(throttledContext);
        Assert.False(throttledLease.IsAcquired, "The 101st refresh request should be throttled");
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/api/v1/auth/verify-otp")]
    [InlineData("/api/v1/auth/forgot-password")]
    public void Auth_endpoints_other_than_refresh_allow_20_requests_per_minute_and_throttle_21st(string path)
    {
        var limiter = CreateGlobalLimiter();
        var ip = IPAddress.Parse("10.0.0.2");

        for (var i = 0; i < 20; i++)
        {
            var context = CreateContext(path, ip);
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired, $"Request {i + 1} on {path} should be permitted");
        }

        var throttledContext = CreateContext(path, ip);
        using var throttledLease = limiter.AttemptAcquire(throttledContext);
        Assert.False(throttledLease.IsAcquired, $"The 21st request on {path} should be throttled");
    }

    [Fact]
    public void Refresh_and_auth_endpoints_are_partitioned_independently_per_ip()
    {
        var limiter = CreateGlobalLimiter();
        var ip = IPAddress.Parse("10.0.0.3");

        // Exhaust the 20-request limit for auth login
        for (var i = 0; i < 20; i++)
        {
            var loginContext = CreateContext("/api/v1/auth/login", ip);
            using var lease = limiter.AttemptAcquire(loginContext);
            Assert.True(lease.IsAcquired);
        }

        var nextLoginContext = CreateContext("/api/v1/auth/login", ip);
        using (var loginLease = limiter.AttemptAcquire(nextLoginContext))
        {
            Assert.False(loginLease.IsAcquired, "Login should now be throttled");
        }

        // Refresh endpoint on the same IP must still permit requests because it uses partition refresh_{ip}
        var refreshContext = CreateContext("/api/v1/auth/refresh", ip);
        using (var refreshLease = limiter.AttemptAcquire(refreshContext))
        {
            Assert.True(refreshLease.IsAcquired, "Refresh requests should NOT be blocked when login limit is exhausted");
        }
    }

    [Fact]
    public void Different_ips_have_isolated_rate_limit_partitions()
    {
        var limiter = CreateGlobalLimiter();
        var ip1 = IPAddress.Parse("192.168.1.1");
        var ip2 = IPAddress.Parse("192.168.1.2");

        for (var i = 0; i < 20; i++)
        {
            var context = CreateContext("/api/v1/auth/login", ip1);
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired);
        }

        // ip1 is exhausted
        using (var lease1 = limiter.AttemptAcquire(CreateContext("/api/v1/auth/login", ip1)))
        {
            Assert.False(lease1.IsAcquired);
        }

        // ip2 should still be allowed
        using (var lease2 = limiter.AttemptAcquire(CreateContext("/api/v1/auth/login", ip2)))
        {
            Assert.True(lease2.IsAcquired);
        }
    }

    [Fact]
    public void Non_auth_endpoints_are_unlimited()
    {
        var limiter = CreateGlobalLimiter();
        var ip = IPAddress.Parse("10.0.0.4");

        for (var i = 0; i < 150; i++)
        {
            var context = CreateContext("/api/v1/courses", ip);
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired, $"Non-auth request {i + 1} should be permitted without rate limiting");
        }
    }

    private static HttpContext CreateContext(string path, IPAddress ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = ip;
        context.Request.Method = "POST";
        context.Request.Path = path;
        return context;
    }
}
