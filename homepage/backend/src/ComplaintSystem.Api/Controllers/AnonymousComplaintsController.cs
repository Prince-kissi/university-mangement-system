using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[AllowAnonymous]
[Route("api/anonymous-complaints")]
public sealed class AnonymousComplaintsController(IComplaintService service, IEvidenceService evidenceService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ComplaintCreatedResponse>> Submit(SubmitAnonymousComplaintRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.SubmitAnonymousAsync(request, cancellationToken));

    [HttpPost("track")]
    public async Task<ActionResult<ComplaintDetailsDto>> Track(AnonymousTrackRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.TrackAnonymousAsync(request, cancellationToken));

    [HttpPost("responses")]
    public async Task<IActionResult> Respond(AnonymousResponseRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.AddAnonymousResponseAsync(request, cancellationToken));

    [HttpPost("{complaintId:guid}/evidence")]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<EvidenceDto>> UploadEvidence(Guid complaintId, IFormFile file,
        [FromHeader(Name = "X-Anonymous-Tracking-Code")] string trackingCode, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return FromResult(await evidenceService.UploadAsync(null, complaintId,
            new EvidenceUpload(file.FileName, file.ContentType, file.Length, content), trackingCode, cancellationToken));
    }
}
