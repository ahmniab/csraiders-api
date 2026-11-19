using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using CsRaiders.Api.Infrastructure.Persistence;
using CsRaiders.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Benchmark data seeder. It is intentionally a separate process so the API can run
// with ASPNETCORE_ENVIRONMENT=Production (no Development auto-migrate / auto-seed).
//
// Configuration (environment variables):
//   ConnectionStrings__Default   required
//   Bench__UserCount             default 20000
//   Bench__Password              default "Bench-Passw0rd!"
//   Bench__EmailPrefix           default "bench"   -> bench+0000000@bench.local
//   Bench__EmailDomain           default "bench.local"
//   Bench__HashVariants          default 64        (Argon2 hashes reused round-robin)
//
// The API and this seeder must share Auth__PasswordPepper__1 so login verifies correctly.

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings__Default is required.");

var userCount = configuration.GetValue("Bench:UserCount", 20_000);
if (userCount < 1)
    throw new InvalidOperationException("Bench:UserCount must be greater than zero.");

var password = configuration["Bench:Password"] ?? "Bench-Passw0rd!";
var emailPrefix = configuration["Bench:EmailPrefix"] ?? "bench";
var emailDomain = configuration["Bench:EmailDomain"] ?? "bench.local";
var hashVariants = Math.Max(1, configuration.GetValue("Bench:HashVariants", 64));

Console.WriteLine(
    $"[seed] users={userCount} prefix={emailPrefix} domain={emailDomain} hashVariants={hashVariants}");

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(120))
    .UseSnakeCaseNamingConvention()
    .Options;

await using var db = new AppDbContext(options);

Console.WriteLine("[seed] applying migrations...");
await db.Database.MigrateAsync();

try
{
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_stat_statements;");
}
catch (Exception ex)
{
    // Not fatal: postgres must have shared_preload_libraries=pg_stat_statements for this
    // to be usable, and the extension needs a superuser. The load test still runs.
    Console.WriteLine($"[seed] warning: pg_stat_statements unavailable: {ex.Message}");
}

Console.WriteLine("[seed] seeding system roles...");
await new IdentitySeeder(db).SeedAsync(allowSessionMultiForStudents: false);

var studentRoleId = await db.Roles
    .Where(r => r.Slug == "student")
    .Select(r => r.Id)
    .SingleAsync();

var likePattern = $"{emailPrefix}+%@{emailDomain}";

var existing = await db.Database
    .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM users WHERE email LIKE {0}", likePattern)
    .SingleAsync();

if (existing >= userCount)
{
    Console.WriteLine($"[seed] {existing} benchmark users already present; nothing to do.");
    Console.WriteLine("[seed] done.");
    return;
}

if (existing > 0)
{
    Console.WriteLine($"[seed] found {existing} partial benchmark users; removing them.");
    await db.Database.ExecuteSqlRawAsync("DELETE FROM users WHERE email LIKE {0}", likePattern);
}

// Hash the shared benchmark password a handful of times instead of once per user.
// Argon2 is deliberately expensive; 20k sequential hashes would take minutes.
Console.WriteLine($"[seed] hashing benchmark password ({hashVariants} variants)...");
var hasher = new Argon2IdPasswordHasher(configuration);
var hashes = new string[hashVariants];

await Parallel.ForEachAsync(
    Enumerable.Range(0, hashVariants),
    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) },
    async (i, ct) =>
    {
        var result = await hasher.HashAsync(password, ct);
        if (result.Outcome != HashOutcome.Success || string.IsNullOrEmpty(result.Hash))
            throw new InvalidOperationException("Argon2 hashing reported capacity exhaustion.");
        hashes[i] = result.Hash;
    });

var now = DateTimeOffset.UtcNow;
const int batchSize = 1_000;

for (var start = 0; start < userCount; start += batchSize)
{
    var end = Math.Min(start + batchSize, userCount);
    var users = new List<User>(end - start);
    var links = new List<UserRole>(end - start);

    for (var i = start; i < end; i++)
    {
        var email = Email.Create($"{emailPrefix}+{i:D7}@{emailDomain}");
        var user = UserFactory.RegisterVerified(email, $"Bench User {i:D7}", hashes[i % hashes.Length], now);

        users.Add(user);
        links.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = studentRoleId,
            GrantedAt = now
        });
    }

    db.Users.AddRange(users);
    db.UserRoles.AddRange(links);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    Console.WriteLine($"[seed] {end}/{userCount} users inserted");
}

var final = await db.Database
    .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM users WHERE email LIKE {0}", likePattern)
    .SingleAsync();

Console.WriteLine($"[seed] done. benchmark users: {final}");
