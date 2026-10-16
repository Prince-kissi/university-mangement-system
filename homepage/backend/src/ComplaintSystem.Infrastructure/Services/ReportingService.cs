using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class ReportingService(ApplicationDbContext dbContext, ICurrentUser currentUser, IAuditService auditService)
    : IReportingService
{
    public async Task<ReportSummaryDto> GetSummaryAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var query = ApplyFilter(filter);
        var byStatusRaw = await query.GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count).ToListAsync(cancellationToken);
        var byStatus = byStatusRaw.Select(x => new NamedCount(x.Status.ToString(), x.Count)).ToList();
        var byCategoryRaw = await query.GroupBy(x => x.Category.Name)
            .Select(x => new { Name = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count).ToListAsync(cancellationToken);
        var byCategory = byCategoryRaw.Select(x => new NamedCount(x.Name, x.Count)).ToList();
        var byDepartmentRaw = await query.GroupBy(x => x.Department.Name)
            .Select(x => new { Name = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count).ToListAsync(cancellationToken);
        var byDepartment = byDepartmentRaw.Select(x => new NamedCount(x.Name, x.Count)).ToList();
        var byPriorityRaw = await query.GroupBy(x => x.Priority)
            .Select(x => new { Priority = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count).ToListAsync(cancellationToken);
        var byPriority = byPriorityRaw.Select(x => new NamedCount(x.Priority.ToString(), x.Count)).ToList();
        var anonymousCount = await query.CountAsync(x => x.IsAnonymous, cancellationToken);
        var identifiedCount = await query.CountAsync(x => !x.IsAnonymous, cancellationToken);
        var timings = await query.Select(x => new { x.CreatedAt, x.FirstRespondedAt, x.ResolvedAt })
            .ToListAsync(cancellationToken);
        var firstResponseHours = timings.Where(x => x.FirstRespondedAt.HasValue)
            .Select(x => (x.FirstRespondedAt!.Value - x.CreatedAt).TotalHours).ToList();
        var resolutionHours = timings.Where(x => x.ResolvedAt.HasValue)
            .Select(x => (x.ResolvedAt!.Value - x.CreatedAt).TotalHours).ToList();
        var now = DateTimeOffset.UtcNow;
        var overdueCount = await query.CountAsync(x => x.ResolutionDueAt < now &&
            x.Status != ComplaintStatus.Resolved && x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Rejected,
            cancellationToken);
        var escalatedCount = await query.CountAsync(x => x.Status == ComplaintStatus.Escalated || x.EscalationLevel > 0, cancellationToken);
        var complaintIds = query.Select(x => x.Id);
        var reopenedCount = await dbContext.ComplaintStatusHistory.CountAsync(x =>
            complaintIds.Contains(x.ComplaintId) && x.NewStatus == ComplaintStatus.Reopened, cancellationToken);
        var departmentRatesRaw = await query.GroupBy(x => x.Department.Name)
            .Select(x => new
            {
                Department = x.Key,
                Total = x.Count(),
                Resolved = x.Count(c => c.Status == ComplaintStatus.Resolved || c.Status == ComplaintStatus.Closed)
            }).ToListAsync(cancellationToken);
        var departmentRates = departmentRatesRaw.Select(x => new DepartmentResolutionRate(x.Department, x.Total,
            x.Resolved, x.Total == 0 ? 0 : decimal.Round((decimal)x.Resolved / x.Total * 100, 2))).ToList();
        var workloadRows = await (from assignment in dbContext.ComplaintAssignments
            join complaint in query on assignment.ComplaintId equals complaint.Id
            join officer in dbContext.Users on assignment.OfficerUserId equals officer.Id
            where assignment.IsCurrent
            group complaint by new { officer.Id, officer.FirstName, officer.LastName } into grouped
            select new
            {
                grouped.Key.FirstName,
                grouped.Key.LastName,
                Active = grouped.Count(x => x.Status != ComplaintStatus.Resolved && x.Status != ComplaintStatus.Closed && x.Status != ComplaintStatus.Rejected),
                Resolved = grouped.Count(x => x.Status == ComplaintStatus.Resolved || x.Status == ComplaintStatus.Closed)
            })
            .OrderByDescending(x => x.Active).ToListAsync(cancellationToken);
        var workloads = workloadRows.Select(x => new OfficerWorkload(
            $"{x.FirstName} {x.LastName}", x.Active, x.Resolved)).ToList();
        var dates = await query.Select(x => x.CreatedAt).ToListAsync(cancellationToken);
        var monthly = dates.GroupBy(x => x.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .OrderBy(x => x.Key).Select(x => new TrendPoint(x.Key, x.Count())).ToList();
        var yearly = dates.GroupBy(x => x.Year).OrderBy(x => x.Key)
            .Select(x => new TrendPoint(x.Key.ToString(CultureInfo.InvariantCulture), x.Count())).ToList();

        return new ReportSummaryDto(byStatus, byCategory, byDepartment, byPriority, anonymousCount,
            identifiedCount, firstResponseHours.Count == 0 ? null : firstResponseHours.Average(),
            resolutionHours.Count == 0 ? null : resolutionHours.Average(), overdueCount, escalatedCount,
            reopenedCount, departmentRates, workloads, monthly, yearly);
    }

    public async Task<ExportFile> ExportAsync(string format, ReportFilter filter, CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(filter, cancellationToken);
        var normalized = format.Trim().ToLowerInvariant();
        var export = normalized switch
        {
            "csv" => new ExportFile($"complaint-report-{DateTime.UtcNow:yyyyMMdd}.csv", "text/csv; charset=utf-8", CreateCsv(summary)),
            "xlsx" or "excel" => new ExportFile($"complaint-report-{DateTime.UtcNow:yyyyMMdd}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", CreateExcel(summary)),
            "pdf" => new ExportFile($"complaint-report-{DateTime.UtcNow:yyyyMMdd}.pdf", "application/pdf", CreatePdf(summary)),
            _ => throw new BadHttpRequestException("Export format must be csv, xlsx or pdf.")
        };
        await auditService.WriteAsync(currentUser.UserId, "ReportExport", "Report", normalized, true,
            new RequestMetadata("system", null), new { format = normalized, filter.From, filter.To, filter.DepartmentId }, cancellationToken);
        return export;
    }

    private IQueryable<Complaint> ApplyFilter(ReportFilter filter)
    {
        var query = dbContext.Complaints.AsNoTracking();
        if (filter.From.HasValue) query = query.Where(x => x.CreatedAt >= filter.From.Value);
        if (filter.To.HasValue) query = query.Where(x => x.CreatedAt <= filter.To.Value);
        if (filter.DepartmentId.HasValue) query = query.Where(x => x.DepartmentId == filter.DepartmentId.Value);
        if (currentUser.IsInRole(AppRoles.DepartmentOfficer))
        {
            if (!currentUser.DepartmentId.HasValue) return query.Where(_ => false);
            query = query.Where(x => x.DepartmentId == currentUser.DepartmentId.Value);
        }
        return query;
    }

    private static byte[] CreateCsv(ReportSummaryDto summary)
    {
        var builder = new StringBuilder("Section,Name,Value\r\n");
        AppendCounts(builder, "Status", summary.ByStatus);
        AppendCounts(builder, "Category", summary.ByCategory);
        AppendCounts(builder, "Department", summary.ByDepartment);
        AppendCounts(builder, "Priority", summary.ByPriority);
        builder.AppendLine($"Overview,Anonymous,{summary.AnonymousCount}");
        builder.AppendLine($"Overview,Identified,{summary.IdentifiedCount}");
        builder.AppendLine($"Overview,Overdue,{summary.OverdueCount}");
        builder.AppendLine($"Overview,Escalated,{summary.EscalatedCount}");
        builder.AppendLine($"Overview,Reopened,{summary.ReopenedCount}");
        return new UTF8Encoding(true).GetBytes(builder.ToString());
    }

    private static void AppendCounts(StringBuilder builder, string section, IEnumerable<NamedCount> counts)
    {
        foreach (var item in counts)
        {
            builder.Append('"').Append(section.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\",\"")
                .Append(item.Name.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\",").Append(item.Count).AppendLine();
        }
    }

    private static byte[] CreateExcel(ReportSummaryDto summary)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Complaint Summary");
        worksheet.Cell(1, 1).Value = "UMaT Complaint Management Report";
        var title = worksheet.Range(1, 1, 1, 3).Merge();
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 16;
        worksheet.Cell(3, 1).Value = "Section";
        worksheet.Cell(3, 2).Value = "Name";
        worksheet.Cell(3, 3).Value = "Count";
        worksheet.Range(3, 1, 3, 3).Style.Font.SetBold();
        var row = 4;
        foreach (var (section, items) in new[]
        {
            ("Status", summary.ByStatus), ("Category", summary.ByCategory),
            ("Department", summary.ByDepartment), ("Priority", summary.ByPriority)
        })
        {
            foreach (var item in items)
            {
                worksheet.Cell(row, 1).Value = section;
                worksheet.Cell(row, 2).Value = item.Name;
                worksheet.Cell(row, 3).Value = item.Count;
                row++;
            }
        }
        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreatePdf(ReportSummaryDto summary)
    {
        var lines = new List<string> { "UMaT Complaint Management Report", $"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC", "" };
        foreach (var (section, items) in new[]
        {
            ("Complaints by status", summary.ByStatus), ("Complaints by category", summary.ByCategory),
            ("Complaints by department", summary.ByDepartment), ("Complaints by priority", summary.ByPriority)
        })
        {
            lines.Add(section);
            lines.AddRange(items.Select(x => $"  {x.Name}: {x.Count}"));
            lines.Add("");
        }
        lines.Add($"Anonymous: {summary.AnonymousCount}   Identified: {summary.IdentifiedCount}");
        lines.Add($"Overdue: {summary.OverdueCount}   Escalated: {summary.EscalatedCount}   Reopened: {summary.ReopenedCount}");
        return SimplePdfWriter.Write(lines.Take(48));
    }

    private static class SimplePdfWriter
    {
        public static byte[] Write(IEnumerable<string> lines)
        {
            var content = new StringBuilder("BT /F1 11 Tf 50 790 Td 14 TL\n");
            foreach (var line in lines)
            {
                var escaped = line.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
                content.Append('(').Append(escaped).Append(") Tj T*\n");
            }
            content.Append("ET");
            var streamContent = content.ToString();
            var objects = new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                $"<< /Length {Encoding.ASCII.GetByteCount(streamContent)} >>\nstream\n{streamContent}\nendstream"
            };
            using var output = new MemoryStream();
            WriteAscii(output, "%PDF-1.4\n");
            var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Length; i++)
            {
                offsets.Add(output.Position);
                WriteAscii(output, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            var xref = output.Position;
            WriteAscii(output, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1)) WriteAscii(output, $"{offset:0000000000} 00000 n \n");
            WriteAscii(output, $"trailer << /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return output.ToArray();
        }

        private static void WriteAscii(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));
    }
}
