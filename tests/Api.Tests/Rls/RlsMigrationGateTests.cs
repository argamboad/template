using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Api.Tests.Rls;

/// <summary>
/// B11-style enforcement gate for ADR-020: on a database built by the REAL migrations (the
/// integration harness), every <c>ITenantScoped</c> table must have row-level security enabled,
/// forced, and carry the tenant-isolation policy. Adding an <c>ITenantScoped</c> entity without
/// shipping its RLS policy migration fails here — the fail-closed property cannot silently regress.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RlsMigrationGateTests(IntegrationTestFactory factory)
{
    [Fact]
    public async Task EveryTenantScopedTable_HasForcedRlsAndPolicy_AfterMigrations()
    {
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
        var tables = RlsDdl.TenantTables(model);
        Assert.NotEmpty(tables);

        await using var conn = new NpgsqlConnection(factory.DatabaseConnectionString);
        await conn.OpenAsync();

        var failures = new List<string>();
        foreach (var (table, _) in tables)
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT c.relrowsecurity,
                       c.relforcerowsecurity,
                       EXISTS (SELECT FROM pg_policies p
                               WHERE p.schemaname = 'public' AND p.tablename = c.relname
                                 AND p.policyname = $2)
                FROM pg_class c
                WHERE c.relname = $1 AND c.relnamespace = 'public'::regnamespace;
                """, conn);
            cmd.Parameters.Add(new NpgsqlParameter { Value = table });
            cmd.Parameters.Add(new NpgsqlParameter { Value = RlsDdl.PolicyName });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                failures.Add($"{table}: table not found in migrated schema");
                continue;
            }
            if (!reader.GetBoolean(0)) failures.Add($"{table}: ROW LEVEL SECURITY not enabled");
            if (!reader.GetBoolean(1)) failures.Add($"{table}: ROW LEVEL SECURITY not FORCEd");
            if (!reader.GetBoolean(2)) failures.Add($"{table}: policy '{RlsDdl.PolicyName}' missing");
        }

        Assert.True(failures.Count == 0,
            "RLS backstop (ADR-020) is incomplete on the migrated schema — every ITenantScoped table "
            + "needs ENABLE + FORCE ROW LEVEL SECURITY and the tenant-isolation policy (add a migration "
            + $"using RlsDdl.StatementsFor):\n - {string.Join("\n - ", failures)}");
    }
}
