using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ComplaintSystem.Api.Controllers;

[Authorize(Policy = "ActiveRole:SuperAdmin")]
[Route("api/system")]
public sealed class SystemManagementController(
    ISystemManagementService service,
    HealthCheckService healthCheckService) : ApiControllerBase
{
    [Authorize(Policy = "Permission:settings.manage")]
    [HttpGet("settings")]
    public Task<IReadOnlyCollection<SettingDto>> Settings(CancellationToken cancellationToken) =>
        service.GetSettingsAsync(cancellationToken);

    [Authorize(Policy = "Permission:settings.manage")]
    [HttpPut("settings/{key}")]
    public async Task<IActionResult> UpdateSetting(string key, UpdateSettingRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.UpdateSettingAsync(CurrentUserId, key, request, RequestMetadata, cancellationToken));

    [Authorize(Policy = "Permission:sessions.manage")]
    [HttpGet("sessions")]
    public Task<IReadOnlyCollection<SessionDto>> Sessions(CancellationToken cancellationToken) =>
        service.GetSessionsAsync(cancellationToken);

    [Authorize(Policy = "Permission:sessions.manage")]
    [HttpPost("sessions/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeSession(Guid id, RevokeSessionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.RevokeSessionAsync(CurrentUserId, id, request.Reason, cancellationToken));

    [Authorize(Policy = "Permission:backups.manage")]
    [HttpGet("backups")]
    public Task<IReadOnlyCollection<BackupDto>> Backups(CancellationToken cancellationToken) =>
        service.GetBackupsAsync(cancellationToken);

    [Authorize(Policy = "Permission:backups.manage")]
    [HttpPost("backups")]
    public async Task<ActionResult<BackupDto>> Backup(CancellationToken cancellationToken) =>
        FromResult(await service.InitiateBackupAsync(CurrentUserId, RequestMetadata, cancellationToken));

    [Authorize(Policy = "Permission:backups.manage")]
    [HttpPost("backups/{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, RestoreBackupRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.RestoreBackupAsync(CurrentUserId, id, request, RequestMetadata, cancellationToken));

    [HttpGet("health")]
    public async Task<ActionResult<HealthSummaryDto>> Health(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken);
        return Ok(new HealthSummaryDto(report.Status.ToString(),
            report.Entries.ToDictionary(x => x.Key, x => x.Value.Status.ToString()), DateTimeOffset.UtcNow));
    }
}
