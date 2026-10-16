using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Route("api/categories")]
public sealed class CategoriesController(IAdministrationService service) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public Task<IReadOnlyCollection<CategoryDto>> Get(CancellationToken cancellationToken) =>
        service.GetCategoriesAsync(cancellationToken);

    [Authorize(Policy = "Permission:categories.manage")]
    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.UpsertCategoryAsync(CurrentUserId, null, request, cancellationToken));

    [Authorize(Policy = "Permission:categories.manage")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, CategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.UpsertCategoryAsync(CurrentUserId, id, request, cancellationToken));

    [Authorize(Policy = "Permission:categories.manage")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await service.DeleteCategoryAsync(CurrentUserId, id, cancellationToken));
}
