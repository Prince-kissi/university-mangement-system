using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.Domain.Entities;

public sealed class ComplaintResponse : EntityBase
{
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public Guid? AuthorUserId { get; set; }
    public required string Body { get; set; }
    public ResponseVisibility Visibility { get; set; } = ResponseVisibility.Public;
    public bool IsAnonymousComplainantResponse { get; set; }
    public string? ResponseSignatureFilePath { get; set; }
    public string? ResponseInitials { get; set; }
    public bool SignatureIncluded { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public Guid? SignedByOfficerId { get; set; }
    public string? SignedByOfficerName { get; set; }
    public string? SignedByAdministrativeUnit { get; set; }
}
