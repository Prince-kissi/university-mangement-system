using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[AllowAnonymous]
[Route("api/setup")]
public sealed class SetupController(ISetupService service) : ApiControllerBase
{
    [HttpGet("status")]
    public Task<SetupStatusDto> Status(CancellationToken cancellationToken) => service.GetStatusAsync(cancellationToken);

    [HttpPost("super-admin")]
    public async Task<ActionResult<FirstSuperAdminCreatedDto>> Create(
        CreateFirstSuperAdminRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateFirstSuperAdminAsync(request, cancellationToken);
        if (result.Succeeded) return Created("/api/setup/status", result.Value);
        if (result.Error!.Code == "setup_complete") return StatusCode(403, new { message = result.Error.Message });
        return BadRequest(new { message = result.Error.Message });
    }
}
