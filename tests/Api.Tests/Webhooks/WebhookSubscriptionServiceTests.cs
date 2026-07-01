using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Entities;
using Template.Infrastructure.Repositories;
using Template.Infrastructure.Webhooks;

namespace Template.Api.Tests.Webhooks;

/// <summary>
/// Drives HOOKS (ADR-016) management: create returns the signing secret once and stores it **encrypted**
/// (not plaintext); URLs and event types are validated; list is tenant-scoped; delete removes. Real Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
public class WebhookSubscriptionServiceTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Creator = Guid.CreateVersion7();

    [Fact]
    public async Task Create_ReturnsSecretOnce_AndStoresEncrypted()
    {
        var tenant = Guid.CreateVersion7();
        var protector = NewProtector();
        await using var db = Fixture.CreateContext(tenant);
        var svc = Build(db, protector);

        var created = await svc.CreateAsync(Creator, "https://example.test/hook", ["ping"], default);

        Assert.NotNull(created);
        Assert.StartsWith("whsec_", created!.Secret);
        Assert.NotEqual(created.Secret, created.Subscription.EncryptedSecret);        // not plaintext
        Assert.Equal(created.Secret, protector.Unprotect(created.Subscription.EncryptedSecret)); // decrypts back
        Assert.Equal("ping", created.Subscription.EventTypes);
        Assert.Equal(tenant, created.Subscription.TenantId);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.test")]
    public async Task Create_InvalidUrl_ReturnsNull(string url)
    {
        var tenant = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext(tenant);
        Assert.Null(await Build(db).CreateAsync(Creator, url, ["ping"], default));
    }

    [Fact]
    public async Task Create_NoEventTypes_DefaultsToKnown()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext(tenant);

        var created = await Build(db).CreateAsync(Creator, "https://example.test/hook", null, default);

        Assert.NotNull(created);
        Assert.Contains(WebhookEvents.Ping, created!.Subscription.EventTypes);
    }

    [Fact]
    public async Task List_IsTenantScoped()
    {
        var mine = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using (var db = Fixture.CreateContext(mine)) await Build(db).CreateAsync(Creator, "https://mine.test/h", ["ping"], default);
        await using (var db = Fixture.CreateContext(other)) await Build(db).CreateAsync(Creator, "https://theirs.test/h", ["ping"], default);

        await using var read = Fixture.CreateContext(mine);
        var list = await Build(read).ListAsync(default);
        Assert.Single(list);
        Assert.Equal("https://mine.test/h", list[0].Url);
    }

    [Fact]
    public async Task Delete_RemovesSubscription()
    {
        var tenant = Guid.CreateVersion7();
        Guid id;
        await using (var db = Fixture.CreateContext(tenant))
        {
            var created = await Build(db).CreateAsync(Creator, "https://example.test/h", ["ping"], default);
            id = created!.Subscription.Id;
        }

        await using (var db = Fixture.CreateContext(tenant))
            Assert.True(await Build(db).DeleteAsync(id, default));

        await using var read = Fixture.CreateContext(tenant);
        Assert.Empty(await read.Set<WebhookSubscription>().ToListAsync());
    }

    private static WebhookSecretProtector NewProtector() => new(new EphemeralDataProtectionProvider());

    private static WebhookSubscriptionService Build(Template.Infrastructure.Persistence.AppDbContext db, WebhookSecretProtector? protector = null) =>
        new(new EfRepository<WebhookSubscription>(db), new TokenGenerator(), protector ?? NewProtector(), TimeProvider.System);
}
