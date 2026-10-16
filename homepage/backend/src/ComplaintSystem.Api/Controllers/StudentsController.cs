using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Route("api/students")]
[Authorize(Roles = AppRoles.Student)]
public sealed class StudentsController(IAuthenticationService authenticationService) : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        var student = await authenticationService.GetCurrentUserAsync(CurrentUserId, cancellationToken);
        return student is null
            ? Problem(statusCode: StatusCodes.Status404NotFound,
                detail: "No student profile was found for this email. Please contact the administrator.")
            : Ok(student);
    }
}
