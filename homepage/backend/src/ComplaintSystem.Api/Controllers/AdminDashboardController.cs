using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize(Policy = "ActiveRole:AdminOrSuper")]
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController(IAdminDashboardService service) : ApiControllerBase
{
    [HttpGet]
    public Task<AdminDashboardDto> Get([FromQuery] AdminDashboardQuery query,
        CancellationToken cancellationToken) => service.GetAsync(CurrentUserId, query, cancellationToken);
}
