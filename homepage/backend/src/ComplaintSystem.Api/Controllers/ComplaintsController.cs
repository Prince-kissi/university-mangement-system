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
