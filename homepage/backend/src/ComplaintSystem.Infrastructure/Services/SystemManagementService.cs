using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class SystemManagementService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IBackupProvider backupProvider,
    IAuditService auditService,
    IClock clock) : ISystemManagementService
{
    private static readonly string[] ForbiddenSettingFragments =
        ["connectionstring", "database.password", "smtp.password", "trackingpepper", "privatekey"];

    public async Task<IReadOnlyCollection<AuditLogDto>> GetAuditLogsAsync(AuditQuery query,
        CancellationToken cancellationToken)
    {
        var logs = dbContext.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Action)) logs = logs.Where(x => x.Action == query.Action);
        if (query.UserId.HasValue) logs = logs.Where(x => x.UserId == query.UserId);
        if (query.From.HasValue) logs = logs.Where(x => x.CreatedAt >= query.From.Value);
        if (query.To.HasValue) logs = logs.Where(x => x.CreatedAt <= query.To.Value);
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        return await logs.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AuditLogDto(x.Id, x.UserId, x.Action, x.CreatedAt, x.RelatedRecordType,
                x.RelatedRecordId, x.IpAddress, x.UserAgent, x.Succeeded, x.MetadataJson))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<SettingDto>> GetSettingsAsync(CancellationToken cancellationToken) =>
        await dbContext.SystemSettings.AsNoTracking().OrderBy(x => x.Key)
            .Select(x => new SettingDto(x.Key, x.IsSensitive ? "********" : x.Value, x.Description, x.IsSensitive))
            .ToListAsync(cancellationToken);

    public async Task<Result> UpdateSettingAsync(Guid actorId, string key, UpdateSettingRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        if (ForbiddenSettingFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Credentials and private key material cannot be managed through the API.");
        }
        var setting = await dbContext.SystemSettings.SingleOrDefaultAsync(x => x.Key == key, cancellationToken)
            ?? throw new NotFoundException("System setting not found.");
        var oldValue = setting.IsSensitive ? "[REDACTED]" : setting.Value;
        setting.Value = request.Value.Trim();
        setting.UpdatedByUserId = actorId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(actorId, "SettingChanged", "SystemSetting", setting.Id.ToString(), true,
            metadata, new { setting.Key, oldValue, newValue = setting.IsSensitive ? "[REDACTED]" : setting.Value, request.Reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyCollection<SessionDto>> GetSessionsAsync(CancellationToken cancellationToken) =>
        await dbContext.ActiveSessions.AsNoTracking().OrderByDescending(x => x.LastSeenAt).Take(500)
            .Select(x => new SessionDto(x.Id, x.UserId, x.IpAddress, x.UserAgent, x.CreatedAt,
                x.LastSeenAt, x.ExpiresAt, x.RevokedAt)).ToListAsync(cancellationToken);

    public async Task<Result> RevokeSessionAsync(Guid actorId, Guid sessionId, string reason,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.ActiveSessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new NotFoundException("Session not found.");
        if (session.RevokedAt is null)
        {
            session.RevokedAt = clock.UtcNow;
            session.RevokedByUserId = actorId;
            session.RevocationReason = reason.Trim();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await auditService.WriteAsync(actorId, "SessionRevoked", "ActiveSession", session.Id.ToString(), true,
            new RequestMetadata("system", null), new { session.UserId, reason }, cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyCollection<BackupDto>> GetBackupsAsync(CancellationToken cancellationToken) =>
        await dbContext.BackupRecords.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new BackupDto(x.Id, x.Reference, x.Status, x.CreatedAt, x.CompletedAt,
                x.SizeBytes, x.FailureMessage)).ToListAsync(cancellationToken);

    public async Task<Result<BackupDto>> InitiateBackupAsync(Guid actorId, RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var backup = new BackupRecord
        {
            Reference = $"UMAT-{clock.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..43],
            InitiatedByUserId = actorId,
            Status = BackupStatus.Running
        };
        dbContext.BackupRecords.Add(backup);
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            var result = await backupProvider.CreateAsync(backup.Reference, cancellationToken);
            backup.StorageReference = result.StorageReference;
            backup.SizeBytes = result.SizeBytes;
            backup.Status = BackupStatus.Completed;
            backup.CompletedAt = clock.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(actorId, "BackupCreated", "BackupRecord", backup.Id.ToString(), true,
                metadata, new { backup.Reference, backup.SizeBytes }, cancellationToken);
            return Result<BackupDto>.Success(ToDto(backup));
        }
        catch (Exception exception)
        {
            backup.Status = BackupStatus.Failed;
            backup.FailureMessage = exception.Message[..Math.Min(exception.Message.Length, 2_000)];
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await auditService.WriteAsync(actorId, "BackupCreated", "BackupRecord", backup.Id.ToString(), false,
                metadata, new { backup.Reference, errorType = exception.GetType().Name }, CancellationToken.None);
            return Result<BackupDto>.Failure("backup_failed", "The backup operation failed. Review server logs.");
        }
    }

    public async Task<Result> RestoreBackupAsync(Guid actorId, Guid backupId, RestoreBackupRequest request,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        if (!await userManager.IsInRoleAsync(actor, AppRoles.SuperAdmin))
            throw new ForbiddenException("Only a Super Admin can restore a backup.");
        if (!await userManager.CheckPasswordAsync(actor, request.Password))
        {
            await auditService.WriteAsync(actorId, "BackupRestore", "BackupRecord", backupId.ToString(), false,
                metadata, new { reason = "PasswordConfirmationFailed" }, cancellationToken);
            return Result.Failure("password_confirmation_failed", "Password confirmation failed.");
        }
        if (!actor.TwoFactorEnabled || !await userManager.VerifyTwoFactorTokenAsync(actor,
            TokenOptions.DefaultAuthenticatorProvider, request.TwoFactorCode.Replace(" ", string.Empty, StringComparison.Ordinal)))
        {
            await auditService.WriteAsync(actorId, "BackupRestore", "BackupRecord", backupId.ToString(), false,
                metadata, new { reason = "TwoFactorFailed" }, cancellationToken);
            return Result.Failure("two_factor_failed", "Two-factor authentication failed.");
        }
        var backup = await dbContext.BackupRecords.SingleOrDefaultAsync(x => x.Id == backupId, cancellationToken)
            ?? throw new NotFoundException("Backup record not found.");
        if (backup.Status != BackupStatus.Completed || string.IsNullOrWhiteSpace(backup.StorageReference))
            return Result.Failure("backup_not_restorable", "Only completed backups can be restored.");
        try
        {
            await backupProvider.RestoreAsync(backup.StorageReference, cancellationToken);
            backup.Status = BackupStatus.Restored;
            backup.RestoredAt = clock.UtcNow;
            backup.RestoredByUserId = actorId;
            backup.RestoreReason = request.WrittenReason.Trim();
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.WriteAsync(actorId, "BackupRestore", "BackupRecord", backup.Id.ToString(), true,
                metadata, new { request.WrittenReason }, cancellationToken);
            return Result.Success();
        }
        catch (Exception exception)
        {
            await auditService.WriteAsync(actorId, "BackupRestore", "BackupRecord", backup.Id.ToString(), false,
                metadata, new { errorType = exception.GetType().Name, request.WrittenReason }, CancellationToken.None);
            return Result.Failure("restore_failed", "The restore operation failed. Review server logs and recovery procedures.");
        }
    }

    private static BackupDto ToDto(BackupRecord x) => new(x.Id, x.Reference, x.Status, x.CreatedAt,
        x.CompletedAt, x.SizeBytes, x.FailureMessage);
}
