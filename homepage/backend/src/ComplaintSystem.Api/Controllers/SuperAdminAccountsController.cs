using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize(Policy = "ActiveRole:SuperAdmin")]
[Route("api/super-admin/accounts")]
public sealed class SuperAdminAccountsController(IUserManagementService service) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserSummaryDto>> Get([FromQuery] AccountQuery query, CancellationToken cancellationToken) =>
        service.GetAccountsAsync(query, cancellationToken);

    [HttpGet("{userId:guid}")]
    public Task<AccountDetailsDto> GetById(Guid userId, CancellationToken cancellationToken) =>
        service.GetAccountAsync(userId, cancellationToken);

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> Update(Guid userId, UpdateAccountRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.UpdateAccountAsync(CurrentUserId, userId, request, RequestMetadata, cancellationToken));

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Archive(Guid userId, [FromBody] AccountActionRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.ArchiveAccountAsync(CurrentUserId, userId, request.Reason, RequestMetadata, cancellationToken));

    [HttpPost("{userId:guid}/restore")]
    public async Task<IActionResult> Restore(Guid userId, AccountActionRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.RestoreAccountAsync(CurrentUserId, userId, request.Reason, RequestMetadata, cancellationToken));
}

public sealed record AccountActionRequest(string Reason);
