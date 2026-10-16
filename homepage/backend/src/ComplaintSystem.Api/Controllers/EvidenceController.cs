using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize]
[Route("api/evidence")]
public sealed class EvidenceController(IEvidenceService service) : ApiControllerBase
{
    [HttpPost("complaint/{complaintId:guid}")]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<EvidenceDto>> Upload(Guid complaintId, IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return FromResult(await service.UploadAsync(CurrentUserId, complaintId,
            new EvidenceUpload(file.FileName, file.ContentType, file.Length, content), null, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> View(Guid id, CancellationToken cancellationToken)
    {
        var download = await service.DownloadAsync(CurrentUserId, id, EvidenceAccessType.View,
            RequestMetadata, cancellationToken);
        return File(download.Content, download.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var download = await service.DownloadAsync(CurrentUserId, id, EvidenceAccessType.Download,
            RequestMetadata, cancellationToken);
        return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
    }

    [Authorize(Policy = "Permission:evidence.manage")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await service.SoftDeleteAsync(CurrentUserId, id, cancellationToken));
}
