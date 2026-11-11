using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.API.Common;
using CsRaiders.Api.API.Filters;
using CsRaiders.Api.API.Services;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.DependencyInjection;
using CsRaiders.Api.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

namespace CsRaiders.Api.API.Extensions;

public static class CsRaidersApiServiceExtensions
{
    /// <summary>
    /// Registers all application services, configuration and validation for the CsRaiders API.
    /// </summary>
    public static WebApplicationBuilder AddCsRaidersApi(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((_, logger) => logger
            .MinimumLevel.Information()
            .WriteTo.Console());

        if (builder.Environment.IsProduction())
        {
            var pepper = builder.Configuration.GetSection("Auth:PasswordPepper").GetChildren().Select(x => x.Value).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(pepper) || pepper.Length < 32 ||
                string.IsNullOrWhiteSpace(builder.Configuration["Auth:OtpHmacKey"]) || builder.Configuration["Auth:OtpHmacKey"]!.Length < 32 ||
                string.IsNullOrWhiteSpace(builder.Configuration["Auth:JwtSigningKey"]) || builder.Configuration["Auth:JwtSigningKey"]!.Length < 32 ||
                string.IsNullOrWhiteSpace(builder.Configuration["Site:Origin"]))
                throw new InvalidOperationException("Auth keys and Site:Origin must be configured with sufficient entropy before startup.");
        }

        builder.Services.AddOptions<AuthOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection(AuthOptions.SectionName).Bind(options);
                configuration.GetSection("Site").Bind(options.Site);
                configuration.GetSection("Proxy").Bind(options.Proxy);
                configuration.GetSection("RateLimit").Bind(options.RateLimit);
                configuration.GetSection("EmailQueue").Bind(options.EmailQueue);
                configuration.GetSection("Crypto").Bind(options.Crypto);
            });

        // ── Services ────────────────────────────────────────────────
        builder.Services.AddControllers();
        builder.Services.AddAuthorization(AuthPolicies.AddAuthPolicies);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddScoped<ISessionCookieWriter, SessionCookieWriter>();
        builder.Services.AddScoped<OriginAndHeaderValidationFilter>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter the access token returned by login. Example: Bearer {token}"
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("bearer", document)] = []
            });
        });

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var proxy in builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [])
                if (IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/api/v1/auth")
                    ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimit:PerMinutePerIp", 20), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("unlimited"));
        });

        var signingKey = builder.Configuration["Auth:JwtSigningKey"] ?? string.Empty;
        var origin = builder.Configuration["Site:Origin"] ?? string.Empty;
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, 
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateIssuer = true, 
                ValidIssuer = origin, 
                ValidateAudience = true, 
                ValidAudience = origin,
                ValidateLifetime = true, 
                ClockSkew = TimeSpan.FromSeconds(30), 
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            };
        });

        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        return builder;
    }
}
