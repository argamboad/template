using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Core.Repositories;

namespace Template.Api.Services;

/// <summary>
/// Per-user in-app notifications (NOTIFY-1, ADR-013). <see cref="NotifyAsync"/> is the seam a feature
/// calls to notify a user; it <b>stages</b> the in-app row on the caller's unit of work (transactional
/// with the triggering change, like <c>IAuditLog</c>) and does not save. The read side (list, unread
/// count, mark read) is scoped to the caller — a user only ever sees/affects their own. In NOTIFY-1 the
/// in-app channel is the only one; the email channel + preferences arrive in NOTIFY-2.
/// </summary>
public interface INotificationService
{
    /// <summary>Stages an in-app notification for a user (commits with the caller's unit of work).</summary>
    Task NotifyAsync(Guid userId, string kind, string title, string body, object? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>The user's notifications, newest first; optionally before a cursor timestamp; capped at 100.</summary>
    Task<IReadOnlyList<Notification>> ListAsync(Guid userId, DateTimeOffset? before, int limit, CancellationToken cancellationToken = default);

    Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Marks one of the user's notifications read. False if it isn't theirs / doesn't exist.</summary>
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class NotificationService(IRepository<Notification> notifications, TimeProvider clock) : INotificationService
{
    public async Task NotifyAsync(Guid userId, string kind, string title, string body, object? metadata = null, CancellationToken cancellationToken = default) =>
        await notifications.AddAsync(new Notification
        {
            UserId = userId,
            Kind = kind,
            Title = title,
            Body = body,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = clock.GetUtcNow(),
        }, cancellationToken);
        // Intentionally no SaveChanges — persists with the caller's unit of work (ADR-013).

    public async Task<IReadOnlyList<Notification>> ListAsync(Guid userId, DateTimeOffset? before, int limit, CancellationToken cancellationToken = default)
    {
        var query = notifications.Query().Where(n => n.UserId == userId);
        if (before is { } cursor)
            query = query.Where(n => n.CreatedAt < cursor);

        return await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        notifications.Query().CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await notifications.Query()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
        if (notification is null)
            return false;

        if (notification.ReadAt is null)
        {
            notification.ReadAt = clock.GetUtcNow();
            notifications.Update(notification);
            await notifications.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return notifications.Query()
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
    }
}
