using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Perezosoft.Api.Authentication;
using Perezosoft.Api.Models;
using Perezosoft.Api.Services;

namespace Perezosoft.Api.Controllers;

/// <summary>
/// The per-user notification center (NOTIFY-1, ADR-013). Every operation is scoped to the authenticated
/// caller (the <see cref="ClaimTypes.NameIdentifier"/> claim) — notifications are <b>per-user</b>, not
/// tenant-scoped, so a user only ever lists or marks their own. Creation is not exposed here: features
/// produce notifications through <see cref="INotificationService.NotifyAsync"/>.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class NotificationsController(INotificationService notifications) : ControllerBase
{
    private Guid? CurrentUserId => User.GetUserId();

    /// <summary>The caller's notifications, newest first. Cursor with <c>before</c>; <c>limit</c> ≤ 100.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTimeOffset? before, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(new ErrorResponse("invalid_token", "Invalid user identity"));

        var items = await notifications.ListAsync(userId, before, limit, cancellationToken);
        return Ok((IReadOnlyList<NotificationResponse>)items.Select(NotificationResponse.From).ToList());
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(new ErrorResponse("invalid_token", "Invalid user identity"));

        return Ok(new UnreadCountResponse { Count = await notifications.UnreadCountAsync(userId, cancellationToken) });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(new ErrorResponse("invalid_token", "Invalid user identity"));

        return await notifications.MarkReadAsync(userId, id, cancellationToken)
            ? NoContent()
            : NotFound(new ErrorResponse("notification_not_found", "Notification not found"));
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(new ErrorResponse("invalid_token", "Invalid user identity"));

        await notifications.MarkAllReadAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>The caller's delivery preferences (defaults to both channels on).</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(new ErrorResponse("invalid_token", "Invalid user identity"));

        var prefs = await notifications.GetPreferencesAsync(userId, cancellationToken);
        return Ok(new NotificationPreferencesResponse { InApp = prefs.InApp, Email = prefs.Email });
    }

    /// <summary>Updates the caller's delivery preferences.</summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest req, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } userId)
            return Unauthorized(new ErrorResponse("invalid_token", "Invalid user identity"));

        await notifications.SetPreferencesAsync(userId, req.InApp, req.Email, cancellationToken);
        return Ok(new NotificationPreferencesResponse { InApp = req.InApp, Email = req.Email });
    }
}
