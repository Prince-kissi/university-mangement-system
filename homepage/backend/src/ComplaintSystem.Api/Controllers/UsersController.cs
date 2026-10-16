using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize]
[Route("api/users")]
public sealed class UsersController(IUserManagementService service) : ApiControllerBase
{
    [Authorize(Policy = "Permission:users.read")]
    [HttpGet]
    public Task<PagedResult<UserSummaryDto>> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        service.GetUsersAsync(page, pageSize, search, cancellationToken);

    [Authorize(Policy = "Permission:users.manage")]
    [HttpPost("staff")]
    public async Task<ActionResult<UserSummaryDto>> CreateStaff(CreateStaffRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.CreateStaffAsync(CurrentUserId, request, RequestMetadata, cancellationToken));

    [Authorize(Policy = "Permission:users.manage")]
    [HttpGet("staff-id-suggestion")]
    public Task<StaffIdSuggestionDto> SuggestStaffId([FromQuery] Guid departmentId,
        CancellationToken cancellationToken) => service.SuggestStaffIdAsync(departmentId, cancellationToken);

    [Authorize(Policy = "Permission:users.manage")]
    [HttpPut("{userId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid userId, UpdateAccountStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.SetAccountStatusAsync(CurrentUserId, userId, request, RequestMetadata, cancellationToken));

    [Authorize(Policy = "Permission:roles.manage")]
    [HttpPut("{userId:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid userId, ChangeUserRoleRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.ChangeRoleAsync(CurrentUserId, userId, request, RequestMetadata, cancellationToken));

    [Authorize(Policy = "Permission:users.manage")]
    [HttpPost("{userId:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid userId, CancellationToken cancellationToken) =>
        FromResult(await service.UnlockAsync(CurrentUserId, userId, RequestMetadata, cancellationToken));

    [Authorize(Policy = "Permission:users.manage")]
    [HttpPost("{userId:guid}/force-password-reset")]
    public async Task<IActionResult> ForcePasswordReset(Guid userId, CancellationToken cancellationToken) =>
        FromResult(await service.ForcePasswordResetAsync(CurrentUserId, userId, RequestMetadata, cancellationToken));

    [Authorize(Roles = AppRoles.SuperAdmin)]
    [Authorize(Policy = "Permission:roles.manage")]
    [HttpGet("role-permissions")]
    public Task<RolePermissionsResponse> GetPermissions(CancellationToken cancellationToken) =>
        service.GetRolePermissionsAsync(CurrentUserId, cancellationToken);

    [Authorize(Roles = AppRoles.SuperAdmin)]
    [Authorize(Policy = "Permission:roles.manage")]
    [HttpPut("role-permissions")]
    public async Task<IActionResult> UpdatePermissions(PermissionUpdateRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.UpdateRolePermissionsAsync(CurrentUserId, request, RequestMetadata, cancellationToken));
}
