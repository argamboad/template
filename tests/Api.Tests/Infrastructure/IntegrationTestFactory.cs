using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
/// startup migrations against the fresh container, exercising the migration path too. Config is
/// overridden last (in-memory) so the test's connection string + Jwt secret win over any repo <c>.env</c>.
/// </summary>
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Long enough for HMAC-SHA256 (JwtSettings requires ≥32 chars); value is irrelevant, only length + stability.
    private const string TestJwtSecret = "integration-test-jwt-secret-key-0123456789";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync(); // must be up before the host is first built (Program migrates on boot)
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();          // tears down the WebApplicationFactory host
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Added last → highest precedence, so the container + test secret override appsettings/.env.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = "Template",
            });
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
