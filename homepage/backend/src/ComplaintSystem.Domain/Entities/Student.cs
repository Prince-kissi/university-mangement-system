using ComplaintSystem.Domain.Common;

namespace ComplaintSystem.Domain.Entities;

public sealed class Student : EntityBase
{
    public Guid? UserId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public string? IndexNumber { get; set; }
    public required string ReferenceNumber { get; set; }
    public string? Department { get; set; }
    public string? Programme { get; set; }
    public string? FacultyOrSchool { get; set; }
    public int? AdmissionYear { get; set; }
    public string? PersonalEmail { get; set; }
    public string? ResidentialAddress { get; set; }
    public string? Level { get; set; }
    public string? TelephoneNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
}
