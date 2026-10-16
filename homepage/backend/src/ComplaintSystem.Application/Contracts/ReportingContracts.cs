namespace ComplaintSystem.Application.Contracts;

public sealed record NamedCount(string Name, int Count);
public sealed record TrendPoint(string Period, int Count);
public sealed record DepartmentResolutionRate(string Department, int Total, int Resolved, decimal Rate);
public sealed record OfficerWorkload(string Officer, int Active, int Resolved);
public sealed record ReportSummaryDto(
    IReadOnlyCollection<NamedCount> ByStatus,
    IReadOnlyCollection<NamedCount> ByCategory,
    IReadOnlyCollection<NamedCount> ByDepartment,
    IReadOnlyCollection<NamedCount> ByPriority,
    int AnonymousCount,
    int IdentifiedCount,
    double? AverageFirstResponseHours,
    double? AverageResolutionHours,
    int OverdueCount,
    int EscalatedCount,
    int ReopenedCount,
    IReadOnlyCollection<DepartmentResolutionRate> DepartmentResolutionRates,
    IReadOnlyCollection<OfficerWorkload> OfficerWorkloads,
    IReadOnlyCollection<TrendPoint> MonthlyTrend,
    IReadOnlyCollection<TrendPoint> YearlyTrend);
public sealed record ReportFilter(DateTimeOffset? From = null, DateTimeOffset? To = null, Guid? DepartmentId = null);
public sealed record ExportFile(string FileName, string ContentType, byte[] Content);
