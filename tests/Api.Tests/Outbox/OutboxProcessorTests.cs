using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Template.Api.Tests.Infrastructure;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Infrastructure.Outbox;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Outbox;

/// <summary>
/// Drives JOBS-1 (ADR-007): the outbox makes side effects atomic with the data change and the
/// dispatcher delivers them reliably (sent, retried, or dead-lettered). Runs against real Postgres
/// because the claim uses <c>FOR UPDATE SKIP LOCKED</c> and the atomicity test needs real
/// transactions — neither is modelled by the EF in-memory provider.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OutboxProcessorTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    // --- atomicity: an enqueued effect commits with the business change, or not at all ---

    [Fact]
    public async Task Enqueue_RolledBack_PersistsNeitherMessageNorBusinessChange()
    {
        var tenant = Guid.CreateVersion7();

        await using (var db = Fixture.CreateContext(tenant))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.Set<TestWidget>().Add(new TestWidget { Name = "business change", TenantId = tenant });
            await new EfOutbox(db, TimeProvider.System).EnqueueAsync("email", "{}");
            await db.SaveChangesAsync();
            // tx disposed without CommitAsync -> rollback
        }

        await using var read = Fixture.CreateContext();
        Assert.Empty(await read.Set<OutboxMessage>().ToListAsync());
        Assert.Empty(await read.Set<TestWidget>().IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Enqueue_Committed_PersistsPendingMessage()
    {
        await using (var db = Fixture.CreateContext())
        {
            await new EfOutbox(db, TimeProvider.System).EnqueueAsync("email", "payload");
            await db.SaveChangesAsync();
        }

        await using var read = Fixture.CreateContext();
        var msg = await read.Set<OutboxMessage>().SingleAsync();
        Assert.Equal("email", msg.Type);
        Assert.Equal(OutboxStatus.Pending, msg.Status);
        Assert.Equal(0, msg.AttemptCount);
    }

    // --- dispatch ---

    [Fact]
    public async Task ProcessDue_DeliversDueMessageToHandler_AndMarksSent()
    {
        await SeedAsync("recording", "hi");
        var handler = new RecordingHandler("recording");

        await using (var db = Fixture.CreateContext())
            Assert.Equal(1, await NewProcessor(db, handler).ProcessDueAsync());

        Assert.Single(handler.Handled);

        await using var read = Fixture.CreateContext();
        var msg = await read.Set<OutboxMessage>().SingleAsync();
        Assert.Equal(OutboxStatus.Sent, msg.Status);
        Assert.NotNull(msg.ProcessedAt);
    }

    [Fact]
    public async Task ProcessDue_SkipsMessagesNotYetDue()
    {
        await SeedAsync("recording", "later", notBefore: TimeSpan.FromMinutes(10));
        var handler = new RecordingHandler("recording");

        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await NewProcessor(db, handler).ProcessDueAsync());
        Assert.Empty(handler.Handled);
    }

    // --- failure handling: retry with backoff, then dead-letter at the attempt cap ---

    [Fact]
    public async Task ProcessDue_FailingHandler_RetriesThenDeadLetters()
    {
        await SeedAsync("boom", "x");
        var handler = new ThrowingHandler("boom");
        // Backoff zero keeps the message immediately due, so a single pass exhausts the attempts.
        var options = new OutboxOptions { MaxAttempts = 3, BackoffBase = TimeSpan.Zero };

        await using (var db = Fixture.CreateContext())
            await NewProcessor(db, handler, options).ProcessDueAsync();

        await using var read = Fixture.CreateContext();
        var msg = await read.Set<OutboxMessage>().SingleAsync();
        Assert.Equal(OutboxStatus.DeadLettered, msg.Status);
        Assert.Equal(3, msg.AttemptCount);
        Assert.NotNull(msg.LastError);
    }

    // --- helpers ---

    private async Task SeedAsync(string type, string payload, TimeSpan? notBefore = null)
    {
        await using var db = Fixture.CreateContext();
        var now = TimeProvider.System.GetUtcNow();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Type = type,
            Payload = payload,
            Status = OutboxStatus.Pending,
            CreatedAt = now,
            NextAttemptAt = now + (notBefore ?? TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
    }

    private static OutboxProcessor NewProcessor(AppDbContext db, IOutboxHandler handler, OutboxOptions? options = null) =>
        new(db, [handler], TimeProvider.System, options ?? new OutboxOptions(), NullLogger<OutboxProcessor>.Instance);
}

internal sealed class RecordingHandler(string type) : IOutboxHandler
{
    public List<OutboxMessage> Handled { get; } = [];
    public string Type => type;

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        Handled.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingHandler(string type) : IOutboxHandler
{
    public string Type => type;

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("handler boom");
}
