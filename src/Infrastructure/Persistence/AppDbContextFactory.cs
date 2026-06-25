using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Template.Core.Abstractions;

namespace Template.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so EF tooling (<c>migrations add</c>,
/// <c>migrations has-pending-model-changes</c>, <c>database update</c>) can build the context
/// WITHOUT booting the API host.
/// <para>
/// EF prefers an <see cref="IDesignTimeDbContextFactory{TContext}"/> in the target/startup project
/// over the application's <c>IHost</c>. Routing design-time through the host made <c>dotnet ef</c>
/// run the API's startup config validation (e.g. <c>Jwt:Secret must be at least 32 characters</c>),
/// which isn't present at design time or in CI — so the host threw and the context couldn't be
/// created. This factory needs only a connection string; model building never reads the current
/// tenant, so a null-tenant stub suffices, and model-diff operations open no DB connection.
/// </para>
/// <para>
/// The connection string comes from <c>ConnectionStrings__DefaultConnection</c> when set (so real
/// <c>database update</c> against a dev/CI Postgres works); otherwise a local placeholder — enough
/// to build the Npgsql model for offline operations like <c>has-pending-model-changes</c>.
/// </para>
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Prefer an explicit env override (e.g. CI/prod); otherwise use the local dev DB. The
        // fallback mirrors src/Api/appsettings.Development.json (the dev connection isn't kept in
        // .env), so `dotnet ef database update` targets the same Postgres the API uses. Keep the
        // two in sync if the dev DB coordinates change.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5433;Database=dev_db;Username=dev;Password=devpassword";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options, NullCurrentTenant.Instance);
    }

    /// <summary>Design-time stub: no request, so no current tenant.</summary>
    private sealed class NullCurrentTenant : ICurrentTenant
    {
        public static readonly NullCurrentTenant Instance = new();
        public Guid? TenantId => null;
    }
}
