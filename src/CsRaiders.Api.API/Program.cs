using CsRaiders.Api.Application.DependencyInjection;
using CsRaiders.Api.API.Common;
using CsRaiders.Api.Infrastructure.DependencyInjection;
using CsRaiders.Api.Infrastructure.Persistence;
using CsRaiders.Api.API.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;
using CsRaiders.Api.API.Services;
using CsRaiders.Api.Application.Common.Abstractions;

var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddScoped<ISessionCookieWriter, SessionCookieWriter>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateIssuer = true, ValidIssuer = origin, ValidateAudience = true, ValidAudience = origin,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
});

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var url in app.Urls)
        app.Logger.LogInformation("Server started and listening on {Url}", url);
});

// ── Middleware pipeline ──────────────────────────────────────
app.UseForwardedHeaders();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSerilogRequestLogging();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(allowSessionMultiForStudents: false);

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
