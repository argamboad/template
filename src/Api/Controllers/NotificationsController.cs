using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Models;
using Template.Api.Services;

namespace Template.Api.Controllers;

/// <summary>
/// The per-user notification center (NOTIFY-1, ADR-013). Every operation is scoped to the authenticated
/// caller (the <see cref="ClaimTypes.NameIdentifier"/> claim) — notifications are <b>per-user</b>, not
/// tenant-scoped, so a user only ever lists or marks their own. Creation is not exposed here: features
/// produce notifications through <see cref="INotificationService.NotifyAsync"/>.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class NotificationsController(INotificationService notifications, IErrorResponseFactory errorFactory) : ControllerBase
{
    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>The caller's notifications, newest first. Cursor with <c>before</c>; <c>limit</c> ≤ 100.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTimeOffset? before, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));

        var items = await notifications.ListAsync(userId, before, limit, cancellationToken);
        return Ok((IReadOnlyList<NotificationResponse>)items.Select(NotificationResponse.From).ToList());
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));

        return Ok(new UnreadCountResponse { Count = await notifications.UnreadCountAsync(userId, cancellationToken) });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));

        return await notifications.MarkReadAsync(userId, id, cancellationToken)
            ? NoContent()
            : NotFound(errorFactory.CreateError("notification_not_found", "Notification not found"));
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(errorFactory.CreateError("invalid_token", "Invalid user identity"));

        await notifications.MarkAllReadAsync(userId, cancellationToken);
        return NoContent();
    }
}
