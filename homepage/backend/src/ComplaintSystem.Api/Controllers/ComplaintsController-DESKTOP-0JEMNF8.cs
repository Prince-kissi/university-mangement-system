using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize]
[Route("api/complaints")]
public sealed class ComplaintsController(IComplaintService service) : ApiControllerBase
{
    [Authorize(Roles = AppRoles.Student)]
    [HttpPost]
    public async Task<ActionResult<ComplaintCreatedResponse>> Submit(SubmitComplaintRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.SubmitAsync(CurrentUserId, request, cancellationToken));

    [HttpGet]
    public Task<PagedResult<ComplaintSummaryDto>> Get([FromQuery] ComplaintQuery query,
        CancellationToken cancellationToken) => service.GetAsync(CurrentUserId, query, cancellationToken);

    [Authorize(Roles = AppRoles.Student)]
    [HttpGet("dashboard")]
    public Task<StudentDashboardDto> Dashboard(CancellationToken cancellationToken) =>
        service.GetStudentDashboardAsync(CurrentUserId, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<ComplaintDetailsDto> GetById(Guid id, CancellationToken cancellationToken) =>
        service.GetByIdAsync(CurrentUserId, id, cancellationToken);

    [Authorize(Roles = AppRoles.Student)]
    [HttpGet("{id:guid}/response.pdf")]
    public async Task<IActionResult> DownloadResponse(Guid id, CancellationToken cancellationToken)
    {
        var complaint = await service.GetByIdAsync(CurrentUserId, id, cancellationToken);
        var pdf = ComplaintResponsePdf.Create(complaint);
        return File(pdf, "application/pdf", $"{complaint.ComplaintId}-response.pdf");
    }

    [Authorize(Policy = "Permission:complaints.assign")]
    [HttpPost("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, AssignComplaintRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.AssignAsync(CurrentUserId, id, request, cancellationToken));

    [Authorize(Policy = "ActiveRole:DepartmentOfficer")]
    [HttpPost("{id:guid}/claim")]
    public async Task<IActionResult> Claim(Guid id, CancellationToken cancellationToken) =>
        FromResult(await service.ClaimAsync(CurrentUserId, id, cancellationToken));

    [Authorize(Policy = "Permission:complaints.assign")]
    [HttpPatch("{id:guid}/reassign")]
    public async Task<IActionResult> Reassign(Guid id, ReassignComplaintRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.ReassignAsync(CurrentUserId, id, request, cancellationToken));

    [Authorize(Policy = "Permission:complaints.assign")]
    [HttpGet("unassigned")]
    public Task<PagedResult<ComplaintSummaryDto>> Unassigned([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        service.GetAsync(CurrentUserId, new ComplaintQuery(Status: ComplaintSystem.Domain.Enums.ComplaintStatus.Unassigned,
            Page: page, PageSize: pageSize), cancellationToken);

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeComplaintStatusRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.ChangeStatusAsync(CurrentUserId, id, request, cancellationToken));

    [Authorize(Policy = "Permission:complaints.assign")]
    [HttpPut("{id:guid}/priority")]
    public async Task<IActionResult> ChangePriority(Guid id, ChangeComplaintPriorityRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.ChangePriorityAsync(CurrentUserId, id, request, cancellationToken));

    [HttpPost("{id:guid}/responses")]
    public async Task<IActionResult> AddResponse(Guid id, AddResponseRequest request, CancellationToken cancellationToken) =>
        FromResult(await service.AddResponseAsync(CurrentUserId, id, request, RequestMetadata, cancellationToken));

    [Authorize(Policy = "ActiveRole:DepartmentOfficer")]
    [HttpPost("{id:guid}/responses/authenticated")]
    [RequestSizeLimit(2 * 1024 * 1024 + 64_000)]
    public async Task<IActionResult> AddAuthenticatedResponse(Guid id, [FromForm] string body,
        [FromForm] string authenticationMode, IFormFile? oneTimeSignature, CancellationToken cancellationToken)
    {
        byte[]? signature = null;
        string? contentType = null;
        if (oneTimeSignature is not null)
        {
            await using var input = oneTimeSignature.OpenReadStream();
            using var memory = new MemoryStream();
            await input.CopyToAsync(memory, cancellationToken);
            signature = memory.ToArray();
            contentType = oneTimeSignature.ContentType;
        }
        return FromResult(await service.AddResponseAsync(CurrentUserId, id,
            new AddResponseRequest(body, authenticationMode, contentType, signature), RequestMetadata, cancellationToken));
    }

    [HttpGet("{id:guid}/responses/{responseId:guid}/signature")]
    public async Task<IActionResult> ResponseSignature(Guid id, Guid responseId, CancellationToken cancellationToken)
    {
        var download = await service.GetResponseSignatureAsync(CurrentUserId, id, responseId, cancellationToken);
        return File(download.Content, download.ContentType);
    }

    [Authorize(Policy = "Permission:complaints.internal-notes")]
    [HttpPost("{id:guid}/internal-notes")]
    public async Task<IActionResult> AddInternalNote(Guid id, AddInternalNoteRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await service.AddInternalNoteAsync(CurrentUserId, id, request, cancellationToken));

    [Authorize(Policy = "Permission:complaints.internal-notes")]
    [HttpGet("{id:guid}/internal-notes")]
    public Task<IReadOnlyCollection<InternalNoteDto>> GetInternalNotes(Guid id, CancellationToken cancellationToken) =>
        service.GetInternalNotesAsync(CurrentUserId, id, cancellationToken);
}
