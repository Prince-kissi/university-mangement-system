using System.Security.Claims;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw new ForbiddenException("An authenticated user is required.");

    protected RequestMetadata RequestMetadata => new(
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        Request.Headers.UserAgent.ToString() is { Length: > 0 } value ? value[..Math.Min(value.Length, 512)] : null);

    protected IActionResult FromResult(Result result) => result.Succeeded
        ? NoContent()
        : BadRequest(new ProblemDetails { Title = result.Error!.Code, Detail = result.Error.Message, Status = 400 });

    protected ActionResult<T> FromResult<T>(Result<T> result) => result.Succeeded
        ? Ok(result.Value)
        : BadRequest(new ProblemDetails { Title = result.Error!.Code, Detail = result.Error.Message, Status = 400 });
}
