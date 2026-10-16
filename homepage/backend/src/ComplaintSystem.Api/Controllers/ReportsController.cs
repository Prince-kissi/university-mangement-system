using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize(Policy = "Permission:reports.view")]
[Route("api/reports")]
public sealed class ReportsController(IReportingService service) : ApiControllerBase
{
    [HttpGet("summary")]
    public Task<ReportSummaryDto> Summary([FromQuery] ReportFilter filter, CancellationToken cancellationToken) =>
        service.GetSummaryAsync(filter, cancellationToken);

    [Authorize(Policy = "Permission:reports.export")]
    [HttpGet("export/{format}")]
    public async Task<IActionResult> Export(string format, [FromQuery] ReportFilter filter,
        CancellationToken cancellationToken)
    {
        var export = await service.ExportAsync(format, filter, cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
    }
}
