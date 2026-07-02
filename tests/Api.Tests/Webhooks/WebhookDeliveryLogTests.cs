using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Entities;
using Template.Infrastructure.Outbox;
using Template.Infrastructure.Repositories;
using Template.Infrastructure.Webhooks;

namespace Template.Api.Tests.Webhooks;

/// <summary>
/// Drives HOOKS-2 (ADR-016): the delivery log + replay. The outbox handler records one
/// <see cref="WebhookDelivery"/> per attempt (success or failure — committed with the message outcome);
/// the read side is tenant-scoped; replay re-enqueues the exact stored payload.
/// </summary>
[Collection(PostgresCollection.Name)]
public class WebhookDeliveryLogTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task Handler_RecordsDelivery_OnSuccess()
    {
        var tenant = Guid.CreateVersion7();
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var subId = await SeedSubscriptionAsync(tenant, protector);

        await using (var db = Fixture.CreateContext())
        {
            var handler = new WebhookOutboxHandler(db, new WebhookSender(new HttpClient(new StubHandler(HttpStatusCode.OK)), new AllowAllUrlGuard()), protector, TimeProvider.System);
            await handler.HandleAsync(Message(tenant, subId, "{}"), default);
            await db.SaveChangesAsync(); // stands in for the OutboxProcessor's commit
        }

        await using var read = Fixture.CreateContext();
        var delivery = Assert.Single(await read.Set<WebhookDelivery>().ToListAsync());
        Assert.True(delivery.Success);
        Assert.Equal(200, delivery.StatusCode);
        Assert.Equal(tenant, delivery.TenantId);
    }

    [Fact]
    public async Task Handler_RecordsDelivery_OnFailure()
    {
        var tenant = Guid.CreateVersion7();
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var subId = await SeedSubscriptionAsync(tenant, protector);

        await using (var db = Fixture.CreateContext())
        {
            var handler = new WebhookOutboxHandler(db, new WebhookSender(new HttpClient(new StubHandler(HttpStatusCode.InternalServerError)), new AllowAllUrlGuard()), protector, TimeProvider.System);
            try { await handler.HandleAsync(Message(tenant, subId, "{}"), default); }
            catch (InvalidOperationException) { /* expected — triggers the outbox retry */ }
            await db.SaveChangesAsync(); // the failed attempt is still recorded
        }

        await using var read = Fixture.CreateContext();
        var delivery = Assert.Single(await read.Set<WebhookDelivery>().ToListAsync());
        Assert.False(delivery.Success);
        Assert.Equal(500, delivery.StatusCode);
    }

    [Fact]
    public async Task Replay_ReenqueuesTheSamePayload()
    {
        var tenant = Guid.CreateVersion7();
        var deliveryId = await SeedDeliveryAsync(tenant, subscriptionId: Guid.CreateVersion7(), eventId: "evt-42", body: "{\"n\":1}");

        await using (var db = Fixture.CreateContext(tenant))
            Assert.True(await BuildService(db, tenant).ReplayAsync(deliveryId, default));

        await using var read = Fixture.CreateContext();
        var message = Assert.Single(await read.Set<OutboxMessage>().Where(m => m.Type == WebhookOutboxHandler.MessageType).ToListAsync());
        var payload = JsonSerializer.Deserialize<WebhookOutboxPayload>(message.Payload)!;
        Assert.Equal("evt-42", payload.EventId);
        Assert.Equal("{\"n\":1}", payload.Body);
    }

    [Fact]
    public async Task Replay_UnknownOrOtherTenant_ReturnsFalse()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext(tenant);
        Assert.False(await BuildService(db, tenant).ReplayAsync(Guid.CreateVersion7(), default));
    }

    [Fact]
    public async Task ListDeliveries_IsTenantScoped()
    {
        var mine = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var sub = Guid.CreateVersion7();
        await SeedDeliveryAsync(mine, sub, "e1", "{}");
        await SeedDeliveryAsync(other, sub, "e2", "{}"); // same subscription id, different tenant

        await using var db = Fixture.CreateContext(mine);
        var list = await BuildService(db, mine).ListDeliveriesAsync(sub, default);
        Assert.Single(list);
        Assert.Equal("e1", list[0].EventId);
    }

    // --- helpers ---

    private static OutboxMessage Message(Guid tenant, Guid subId, string body) => new()
    {
        Type = WebhookOutboxHandler.MessageType,
        TenantId = tenant,
        Payload = JsonSerializer.Serialize(new WebhookOutboxPayload(subId, "ping", "e1", body)),
        CreatedAt = DateTimeOffset.UtcNow,
        NextAttemptAt = DateTimeOffset.UtcNow,
    };

    private static WebhookSubscriptionService BuildService(Template.Infrastructure.Persistence.AppDbContext db, Guid tenant) =>
        new(new EfRepository<WebhookSubscription>(db), new EfRepository<WebhookDelivery>(db),
            new EfOutbox(db, TimeProvider.System), new TestCurrentTenant { TenantId = tenant },
            new TokenGenerator(), new WebhookSecretProtector(new EphemeralDataProtectionProvider()),
            new AllowAllUrlGuard(), TimeProvider.System);

    private async Task<Guid> SeedSubscriptionAsync(Guid tenant, WebhookSecretProtector protector)
    {
        var sub = new WebhookSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Url = "https://recv.test/h",
            EventTypes = "ping",
            EncryptedSecret = protector.Protect("whsec_" + Guid.NewGuid().ToString("N")),
            CreatedByUserId = Guid.CreateVersion7(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var db = Fixture.CreateContext(tenant);
        db.Set<WebhookSubscription>().Add(sub);
        await db.SaveChangesAsync();
        return sub.Id;
    }

    private async Task<Guid> SeedDeliveryAsync(Guid tenant, Guid subscriptionId, string eventId, string body)
    {
        var delivery = new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            SubscriptionId = subscriptionId,
            EventType = "ping",
            EventId = eventId,
            Body = body,
            Success = false,
            StatusCode = 500,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var db = Fixture.CreateContext(); // not ITenantScoped — no ambient tenant needed
        db.Set<WebhookDelivery>().Add(delivery);
        await db.SaveChangesAsync();
        return delivery.Id;
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
