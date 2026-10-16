using ComplaintSystem.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace ComplaintSystem.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? StaffId { get; set; }
    public string? StudentReferenceNumber { get; set; }
    public string? StudentIndexNumber { get; set; }
    public string? StudentDepartment { get; set; }
    public string? StudentLevel { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public AccountStatus AccountStatus { get; set; } = AccountStatus.PendingVerification;
    public bool IsVerified { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationMethod { get; set; }
    public bool ForcePasswordReset { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? SignatureFilePath { get; set; }
    public string? Initials { get; set; }
    public DateTimeOffset? SignatureUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public string DisplayName => $"{FirstName} {LastName}";
}
