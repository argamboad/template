using Microsoft.EntityFrameworkCore;
using Npgsql;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Api.Tests.Rls;

/// <summary>
/// Provisions what the RLS backstop (ADR-020) needs on a test database: the non-privileged runtime
/// role (local containers connect as a superuser, which Postgres exempts from RLS entirely — so the
/// backstop is only observable through a dedicated role) and the model-derived policies from
/// <see cref="RlsDdl"/>. Idempotent; safe to run per test.
/// </summary>
public static class RlsTestSetup
{
    public const string RuntimeRole = "app_runtime";
    public const string RuntimePassword = "rls-test-password";

    /// <summary>Connection string for the same database, connecting as the runtime role.</summary>
    public static string RuntimeConnectionString(string superuserConnectionString) =>
        new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = RuntimeRole,
            Password = RuntimePassword,
        }.ConnectionString;

    /// <summary>Creates the runtime role (idempotent), grants it CRUD, and applies the RLS DDL.</summary>
    public static async Task ProvisionAsync(DbContext db)
    {
        var statements = new List<string>
        {
            $"""
             DO $$
             BEGIN
                 IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{RuntimeRole}') THEN
                     CREATE ROLE {RuntimeRole} LOGIN PASSWORD '{RuntimePassword}';
                 END IF;
             END $$;
             """,
            $"GRANT USAGE ON SCHEMA public TO {RuntimeRole};",
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {RuntimeRole};",
            $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {RuntimeRole};",
        };
        statements.AddRange(RlsDdl.StatementsFor(db.Model));

        foreach (var sql in statements)
#pragma warning disable EF1002 // no user input — role name/password are test constants, DDL is model-derived
            await db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }
}
