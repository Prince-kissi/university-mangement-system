using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class NotificationService(
    ApplicationDbContext dbContext,
    IAuditService auditService) : INotificationService
{
    public async Task<IReadOnlyCollection<NotificationDto>> GetForUserAsync(Guid userId, bool unreadOnly,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.AsNoTracking().Where(x => x.UserId == userId);
        if (unreadOnly) query = query.Where(x => !x.IsRead);
        return await query.OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new NotificationDto(x.Id, x.Title, x.Message, x.Type, x.Link, x.IsRead, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, cancellationToken);
        if (notification is null) return;
        notification.IsRead = true;
        notification.ReadAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AnnouncementResultDto> PreviewAnnouncementAsync(Guid actorId, AnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        var userIds = await ResolveRecipientsAsync(actorId, request, cancellationToken);
        return new AnnouncementResultDto(userIds.Count, 0, 0, "Preview");
    }

    public async Task<AnnouncementResultDto> SendAnnouncementAsync(Guid actorId, AnnouncementRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var userIds = await ResolveRecipientsAsync(actorId, request, cancellationToken);
        if (request.SaveAsDraft)
        {
            await auditService.WriteAsync(actorId, "AnnouncementDrafted", "Announcement", null, true, metadata,
                new { recipients = userIds.Count, request.RecipientRoles, request.DepartmentId, request.Category, request.Priority, request.DeliveryChannels }, cancellationToken);
            return new AnnouncementResultDto(userIds.Count, 0, 0, "Draft");
        }

        var displayMessage = $"[{request.Priority}] {request.Category}\n{request.Message.Trim()}";
        dbContext.Notifications.AddRange(userIds.Select(userId => new Notification
        {
            UserId = userId,
            Title = request.Title.Trim(),
            Message = displayMessage,
            Type = request.Category == "Security Alert" || request.Priority == "Urgent"
                ? NotificationType.Security : NotificationType.Announcement
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(actorId, "AnnouncementSent", "Announcement", null, true,
            metadata, new { recipients = userIds.Count, request.Everyone, request.RecipientRoles, request.DepartmentId,
                request.StudentLevel, request.UserIds, request.Category, request.Priority, request.DeliveryChannels,
                request.PublishAt, request.ExpiresAt, request.RequireAcknowledgement, status = "Sent" }, cancellationToken);
        return new AnnouncementResultDto(userIds.Count, userIds.Count, 0, "Sent");
    }

    private async Task<List<Guid>> ResolveRecipientsAsync(Guid actorId, AnnouncementRequest request, CancellationToken cancellationToken)
    {
        var actorRoles = await (from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == actorId select role.Name!).ToListAsync(cancellationToken);
        var isSuper = actorRoles.Contains(AppRoles.SuperAdmin);
        var isAdmin = actorRoles.Contains(AppRoles.Admin);
        var isOfficer = actorRoles.Contains(AppRoles.DepartmentOfficer);
        if (!isSuper && !isAdmin && !isOfficer) throw new ForbiddenException("Only authorized staff can create announcements.");

        var requestedRoles = request.Everyone ? AppRoles.All.ToList() : (request.RecipientRoles ?? []).Distinct().ToList();
        if (requestedRoles.Any(x => !AppRoles.All.Contains(x, StringComparer.Ordinal)))
            throw new ForbiddenException("One or more recipient groups are invalid.");
        if (isOfficer && (request.Everyone || requestedRoles.Any(x => x is AppRoles.Admin or AppRoles.SuperAdmin)))
            throw new ForbiddenException("Department Officers may send announcements only to students and Department Officers.");

        var activeUsers = dbContext.Users.AsNoTracking().Where(x => !x.IsDeleted && x.AccountStatus == AccountStatus.Active);
        IQueryable<Guid> roleRecipients = Enumerable.Empty<Guid>().AsQueryable();
        if (requestedRoles.Count > 0)
        {
            roleRecipients = from user in activeUsers
                join userRole in dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where requestedRoles.Contains(role.Name!)
                select user.Id;
        }
        var recipients = requestedRoles.Count == 0 ? [] : await roleRecipients.Distinct().ToListAsync(cancellationToken);
        if (request.DepartmentId.HasValue)
        {
            var inUnit = await activeUsers.Where(x => x.DepartmentId == request.DepartmentId).Select(x => x.Id).ToListAsync(cancellationToken);
            recipients = recipients.Intersect(inUnit).ToList();
        }
        if (!string.IsNullOrWhiteSpace(request.StudentLevel))
        {
            var atLevel = await activeUsers.Where(x => x.StudentLevel == request.StudentLevel).Select(x => x.Id).ToListAsync(cancellationToken);
            recipients = recipients.Intersect(atLevel).ToList();
        }
        if (request.UserIds is { Count: > 0 })
        {
            var selected = await activeUsers.Where(x => request.UserIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken);
            if (isOfficer)
            {
                var permitted = await (from userRole in dbContext.UserRoles.AsNoTracking()
                    join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                    where selected.Contains(userRole.UserId) && (role.Name == AppRoles.Student || role.Name == AppRoles.DepartmentOfficer)
                    select userRole.UserId).Distinct().ToListAsync(cancellationToken);
                if (permitted.Count != selected.Count) throw new ForbiddenException("The selected users include recipients outside the Department Officer policy.");
            }
            recipients.AddRange(selected);
        }
        return recipients.Distinct().ToList();
    }

    public async Task SendToUserAsync(Guid userId, string title, string message, NotificationType type,
        string? link, CancellationToken cancellationToken)
    {
        dbContext.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Link = link
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
