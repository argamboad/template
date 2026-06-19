using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Template.Api.Tests.Infrastructure;

/// <summary>
/// Spins up a real PostgreSQL instance in a throwaway container for the whole test
/// run. Real Postgres (not the EF in-memory provider) is required because several
/// services branch on <c>Database.IsRelational()</c> and use relational-only
/// constructs — <c>ExecuteUpdateAsync</c>, savepoints — that the in-memory provider
/// silently skips. Requires a running Docker daemon (the dev box already runs one
/// for <c>docker compose</c>).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        // Build the schema from the EF model (fast; no migration history needed for tests).
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A fresh context bound to the container. The caller disposes it.</summary>
    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Truncates every table so each test starts from a clean slate. Call at the top
    /// of a test (or in the test class constructor) when tests share the container.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "TenantInvitations", "TenantMemberships", "UserLogins",
                          "RefreshTokens", "LoginTokens", "Users", "Tenants"
            RESTART IDENTITY CASCADE;
            """);
    }
}

/// <summary>
/// xUnit collection so the (relatively expensive) container is created once and
/// shared by every relational test class. Decorate such classes with
/// <c>[Collection(PostgresCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
