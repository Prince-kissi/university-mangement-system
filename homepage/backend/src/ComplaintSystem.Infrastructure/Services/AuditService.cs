using System.Text.Json;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Infrastructure.Persistence;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class AuditService(ApplicationDbContext dbContext, IClock clock) : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(Guid? userId, string action, string recordType, string? recordId,
        bool succeeded, RequestMetadata metadata, object? structuredMetadata, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            RelatedRecordType = recordType,
            RelatedRecordId = recordId,
            IpAddress = metadata.IpAddress,
            UserAgent = metadata.UserAgent,
            Succeeded = succeeded,
            MetadataJson = JsonSerializer.Serialize(structuredMetadata ?? new { }, JsonOptions),
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
