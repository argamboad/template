using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Api.Services;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Template.Api.Tests.Infrastructure;

/// <summary>
/// v2 audit B8-6: the HTTP integration harness. Boots the REAL app (<c>Program</c>) via
/// <see cref="WebApplicationFactory{T}"/> against a throwaway Postgres container — so tests exercise the
/// full pipeline (JWT-bearer auth → tenant filter → controllers → EF → Postgres), not hand-assembled
/// services. This is what lets the MFA step-up (B8-2) and controller-split (B9-1) work be proven at the
/// wire, and de-risks routing/DI refactors by asserting routes stay stable end-to-end.
///
/// The app boots in Development (so the fake billing provider is allowed — B1) and runs its real
/// startup migrations against the fresh container, exercising the migration path too.
///
/// Two seams are needed because <c>Program</c> reads config <b>before</b> <c>builder.Build()</c> (so the
/// factory's Build-time config hooks are too late): (1) <c>Jwt:Secret</c> is supplied as an environment
/// variable so it exists at CreateBuilder time — its value is irrelevant, since minting and validation
/// both resolve the app's own settings; (2) the <c>AppDbContext</c> registration is replaced in
/// <c>ConfigureTestServices</c> to point at the throwaway container, which is env-independent and (unlike
/// a config override) can't be defeated by a repo <c>.env</c>, so the container is always the DB used.
/// </summary>
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Long enough for HMAC-SHA256 (JwtSettings requires ≥32 chars); value is irrelevant, only length + validity.
    private const string TestJwtSecret = "integration-test-jwt-secret-key-0123456789";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public IntegrationTestFactory()
    {
        // Must be present when Program reads Jwt:Secret at CreateBuilder time (before Build) — env vars
        // are a CreateBuilder config source; the WebApplicationFactory config hooks run too late.
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync(); // must be up before the host is first built (Program migrates on boot)
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();          // tears down the WebApplicationFactory host
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            // Repoint the DbContext at the throwaway container — bulletproof regardless of config/.env.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));
        });
    }

    /// <summary>Seeds a tenant + a user who owns it, and returns the identifiers a token needs.</summary>
    public async Task<SeededUser> SeedUserAsync(string role = TenantRoles.Owner, string? email = null)
    {
        email ??= $"user-{Guid.CreateVersion7():N}@test.local";
        var tenant = new Tenant { Name = "Test Household", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new User { Email = email, DisplayName = "Test User", EmailVerified = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Role = role, JoinedAt = DateTimeOffset.UtcNow };

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<Tenant>().Add(tenant);
        db.Set<User>().Add(user);
        db.Set<TenantMembership>().Add(membership);
        await db.SaveChangesAsync();

        return new SeededUser(user.Id, tenant.Id, email, role);
    }

    /// <summary>Mints a real access token for a seeded user via the app's own token service.</summary>
    public string IssueAccessToken(SeededUser user)
    {
        using var scope = Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return tokens.IssueAccessToken(user.UserId, user.Email, provider: "test", tenantId: user.TenantId);
    }

    /// <summary>An HttpClient with a valid bearer token for the given seeded user.</summary>
    public HttpClient CreateClientFor(SeededUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", IssueAccessToken(user));
        return client;
    }
}

/// <summary>Identifiers for a seeded user, enough to mint a token and assert tenant scoping.</summary>
public sealed record SeededUser(Guid UserId, Guid TenantId, string Email, string Role);

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationTestFactory>
{
    public const string Name = "integration";
}
