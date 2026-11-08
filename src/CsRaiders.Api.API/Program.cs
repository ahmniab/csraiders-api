using CsRaiders.Api.Application.DependencyInjection;
using CsRaiders.Api.API.Common;
using CsRaiders.Api.Infrastructure.DependencyInjection;
using CsRaiders.Api.Infrastructure.Persistence;
using CsRaiders.Api.API.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(allowSessionMultiForStudents: false);

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
