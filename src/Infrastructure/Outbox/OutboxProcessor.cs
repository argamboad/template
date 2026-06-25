using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Outbox;

/// <summary>
/// The testable core of the dispatcher: claims due outbox messages and records each outcome —
/// sent, retry-with-backoff, or dead-lettered after <see cref="OutboxOptions.MaxAttempts"/>.
/// <para>
/// Each message is claimed in its own transaction with Postgres <c>FOR UPDATE SKIP LOCKED</c>, so
/// concurrent pollers never double-claim a row and the lock is scoped to a single handler
/// invocation (ADR-007). Kept separate from the <see cref="OutboxDispatcher"/>
/// <c>BackgroundService</c> so it can be unit-tested without timing.
/// </para>
/// </summary>
public sealed class OutboxProcessor(
    AppDbContext db,
    IEnumerable<IOutboxHandler> handlers,
    TimeProvider clock,
    OutboxOptions options,
    ILogger<OutboxProcessor> logger)
{
    private readonly Dictionary<string, IOutboxHandler> _handlers = handlers.ToDictionary(h => h.Type);

    /// <summary>Processes up to <see cref="OutboxOptions.BatchSize"/> due messages; returns how many were handled.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var processed = 0;
        for (var i = 0; i < options.BatchSize; i++)
        {
            if (!await ProcessNextAsync(cancellationToken)) break;
            processed++;
        }
        return processed;
    }

    /// <summary>Claims and processes a single due message. Returns false when nothing is due.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
            throw new InvalidOperationException("The outbox requires a relational provider (FOR UPDATE SKIP LOCKED).");

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Claim the oldest due+pending row, skipping any another poller already holds. The raw SQL
        // orders and LIMITs internally (so at most one row returns); materialize with ToList to keep
        // EF from layering its own order-less First operator on top — which only logs a noisy,
        // false-positive "FirstOrDefault without OrderBy" warning on every poll.
        var message = (await db.Set<OutboxMessage>()
            .FromSql($"""
                SELECT * FROM "OutboxMessages"
                WHERE "Status" = {OutboxStatus.Pending} AND "NextAttemptAt" <= {now}
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)).FirstOrDefault();

        if (message is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return false;
        }

        try
        {
            if (!_handlers.TryGetValue(message.Type, out var handler))
                throw new InvalidOperationException($"No IOutboxHandler registered for outbox type '{message.Type}'.");

            await handler.HandleAsync(message, cancellationToken);
            message.Status = OutboxStatus.Sent;
            message.ProcessedAt = now;
            message.LastError = null;
        }
        catch (Exception ex)
        {
            message.AttemptCount++;
            message.LastError = Truncate(ex.Message, 1000);
            if (message.AttemptCount >= options.MaxAttempts)
            {
                message.Status = OutboxStatus.DeadLettered;
                logger.LogError(ex, "Outbox message {Id} ({Type}) dead-lettered after {Attempts} attempt(s)",
                    message.Id, message.Type, message.AttemptCount);
            }
            else
            {
                var delay = Backoff(message.AttemptCount);
                message.NextAttemptAt = now + delay;
                logger.LogWarning(ex, "Outbox message {Id} ({Type}) failed (attempt {Attempts}); retrying after {Delay}",
                    message.Id, message.Type, message.AttemptCount, delay);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    private TimeSpan Backoff(int attempt) => options.BackoffBase * Math.Pow(2, attempt - 1);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
