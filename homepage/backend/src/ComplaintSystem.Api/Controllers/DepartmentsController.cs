using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Route("api/departments")]
public sealed class DepartmentsController(IAdministrationService service) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public Task<IReadOnlyCollection<DepartmentDto>> Get(CancellationToken cancellationToken) =>
        service.GetDepartmentsAsync(cancellationToken);

    [Authorize(Policy = "Permission:departments.manage")]
    [HttpPost]
    public async Task<ActionResult<DepartmentDto>> Create(DepartmentRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.UpsertDepartmentAsync(CurrentUserId, null, request, cancellationToken));

    [Authorize(Policy = "Permission:departments.manage")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DepartmentDto>> Update(Guid id, DepartmentRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.UpsertDepartmentAsync(CurrentUserId, id, request, cancellationToken));

    [Authorize(Policy = "Permission:departments.manage")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await service.DeleteDepartmentAsync(CurrentUserId, id, cancellationToken));
}
