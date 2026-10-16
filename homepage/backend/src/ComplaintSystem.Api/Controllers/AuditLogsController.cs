using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize(Policy = "Permission:audit.read")]
[Route("api/audit-logs")]
public sealed class AuditLogsController(ISystemManagementService service) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<AuditLogDto>> Get([FromQuery] AuditQuery query,
        CancellationToken cancellationToken) => service.GetAuditLogsAsync(query, cancellationToken);
}
