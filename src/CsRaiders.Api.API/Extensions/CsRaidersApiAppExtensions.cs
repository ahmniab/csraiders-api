using CsRaiders.Api.API.Middleware;
using CsRaiders.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace CsRaiders.Api.API.Extensions;

public static class CsRaidersApiAppExtensions
{
    /// <summary>
    /// Configures the full middleware pipeline and endpoints for the CsRaiders API.
    /// </summary>
    public static async Task UseCsRaidersApi(this WebApplication app)
    {
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
    }
}
