using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationService service) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<NotificationDto>> Get([FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default) =>
        service.GetForUserAsync(CurrentUserId, unreadOnly, cancellationToken);

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await service.MarkReadAsync(CurrentUserId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("announcements/preview")]
    public async Task<ActionResult<AnnouncementResultDto>> Preview(AnnouncementRequest request, CancellationToken cancellationToken) =>
        Ok(await service.PreviewAnnouncementAsync(CurrentUserId, request, cancellationToken));

    [HttpPost("announcements")]
    public async Task<ActionResult<AnnouncementResultDto>> Announce(AnnouncementRequest request, CancellationToken cancellationToken)
    {
        return Accepted(await service.SendAnnouncementAsync(CurrentUserId, request, RequestMetadata, cancellationToken));
    }
}
