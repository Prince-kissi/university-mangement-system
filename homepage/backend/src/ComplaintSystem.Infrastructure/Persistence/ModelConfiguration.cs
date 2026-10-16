using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Persistence;

internal static class ModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        ConfigureUsers(builder);
        ConfigureStudents(builder);
        ConfigureDepartments(builder);
        ConfigureComplaints(builder);
        ConfigureSecurityAndOperations(builder);

        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(x => typeof(EntityBase).IsAssignableFrom(x.ClrType)))
        {
            builder.Entity(entityType.ClrType).Property(nameof(EntityBase.ConcurrencyToken)).IsConcurrencyToken();
            builder.Entity(entityType.ClrType).Property(nameof(EntityBase.CreatedAt)).HasColumnType("timestamp with time zone");
            builder.Entity(entityType.ClrType).Property(nameof(EntityBase.UpdatedAt)).HasColumnType("timestamp with time zone");
        }
    }

    private static void ConfigureStudents(ModelBuilder builder)
    {
        var student = builder.Entity<Student>();
        student.ToTable("Students");
        student.Property(x => x.FirstName).HasMaxLength(80);
        student.Property(x => x.LastName).HasMaxLength(80);
        student.Property(x => x.Email).HasMaxLength(254);
        student.Property(x => x.IndexNumber).HasMaxLength(32);
        student.Property(x => x.ReferenceNumber).HasMaxLength(10);
        student.Property(x => x.Department).HasMaxLength(150);
        student.Property(x => x.Programme).HasMaxLength(180);
        student.Property(x => x.FacultyOrSchool).HasMaxLength(180);
        student.Property(x => x.PersonalEmail).HasMaxLength(254);
        student.Property(x => x.ResidentialAddress).HasMaxLength(500);
        student.Property(x => x.Level).HasMaxLength(3);
        student.Property(x => x.TelephoneNumber).HasMaxLength(24);
        student.Property(x => x.Gender).HasMaxLength(10);
        student.HasIndex(x => x.Email).IsUnique();
        student.HasIndex(x => x.ReferenceNumber).IsUnique();
        student.HasIndex(x => x.IndexNumber).IsUnique().HasFilter("\"IndexNumber\" IS NOT NULL");
        student.HasIndex(x => x.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
        student.HasOne<ApplicationUser>().WithOne().HasForeignKey<Student>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureUsers(ModelBuilder builder)
    {
        var user = builder.Entity<ApplicationUser>();
        user.Property(x => x.FirstName).HasMaxLength(80);
        user.Property(x => x.Title).HasMaxLength(20);
        user.Property(x => x.MiddleName).HasMaxLength(80);
        user.Property(x => x.LastName).HasMaxLength(80);
        user.Property(x => x.StudentReferenceNumber).HasMaxLength(10);
        user.Property(x => x.StudentIndexNumber).HasMaxLength(32);
        user.Property(x => x.StaffId).HasMaxLength(32);
        user.Property(x => x.StudentDepartment).HasMaxLength(150);
        user.Property(x => x.StudentLevel).HasMaxLength(3);
        user.Property(x => x.PhoneNumber).HasMaxLength(24);
        user.Property(x => x.Gender).HasMaxLength(10);
        user.Property(x => x.NationalId).HasMaxLength(40);
        user.Property(x => x.Position).HasMaxLength(120);
        user.Property(x => x.OfficeLocation).HasMaxLength(150);
        user.Property(x => x.RecoveryEmail).HasMaxLength(254);
        user.Property(x => x.AlternativePhoneNumber).HasMaxLength(24);
        user.Property(x => x.IdentityProvider).HasMaxLength(32);
        user.Property(x => x.ProviderSubject).HasMaxLength(255);
        user.Property(x => x.ProfileImageUrl).HasMaxLength(2_000);
        user.Property(x => x.SignatureFilePath).HasMaxLength(1_000);
        user.Property(x => x.Initials).HasMaxLength(6);
        user.Property(x => x.AccountStatus).HasConversion<string>().HasMaxLength(32);
        user.Property(x => x.VerificationMethod).HasMaxLength(16);
        user.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        user.HasIndex(x => x.StudentReferenceNumber).IsUnique().HasFilter("\"StudentReferenceNumber\" IS NOT NULL");
        user.HasIndex(x => x.StudentIndexNumber).IsUnique().HasFilter("\"StudentIndexNumber\" IS NOT NULL");
        user.HasIndex(x => x.StaffId).IsUnique().HasFilter("\"StaffId\" IS NOT NULL");
        user.HasIndex(x => new { x.IdentityProvider, x.ProviderSubject }).IsUnique()
            .HasFilter("\"IdentityProvider\" IS NOT NULL AND \"ProviderSubject\" IS NOT NULL");
        user.HasIndex(x => new { x.DepartmentId, x.AccountStatus });
    }

    private static void ConfigureDepartments(ModelBuilder builder)
    {
        var department = builder.Entity<Department>();
        department.Property(x => x.Code).HasMaxLength(20);
        department.Property(x => x.Name).HasMaxLength(150);
        department.Property(x => x.ContactEmail).HasMaxLength(254);
        department.HasIndex(x => x.Code).IsUnique();
        department.HasIndex(x => x.Name).IsUnique();
        department.HasQueryFilter(x => !x.IsDeleted);

        var category = builder.Entity<ComplaintCategory>();
        category.Property(x => x.Name).HasMaxLength(150);
        category.Property(x => x.Description).HasMaxLength(1_000);
        category.Property(x => x.DefaultPriority).HasConversion<string>().HasMaxLength(16);
        category.HasIndex(x => new { x.Name, x.DepartmentId }).IsUnique();
        category.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        category.HasQueryFilter(x => !x.IsDeleted);
    }

    private static void ConfigureComplaints(ModelBuilder builder)
    {
        var complaint = builder.Entity<Complaint>();
        complaint.Property(x => x.PublicId).HasMaxLength(32);
        complaint.Property(x => x.Title).HasMaxLength(180);
        complaint.Property(x => x.Description).HasMaxLength(10_000);
        complaint.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
        complaint.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        complaint.HasIndex(x => x.PublicId).IsUnique();
        complaint.HasIndex(x => new { x.DepartmentId, x.Status });
        complaint.HasIndex(x => new { x.StudentUserId, x.CreatedAt });
        complaint.HasIndex(x => x.ResolutionDueAt);
        complaint.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        complaint.HasOne(x => x.Department).WithMany(x => x.Complaints).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        complaint.HasQueryFilter(x => !x.IsDeleted);

        var assignment = builder.Entity<ComplaintAssignment>();
        assignment.Property(x => x.Reason).HasMaxLength(1_000);
        assignment.HasIndex(x => new { x.ComplaintId, x.IsCurrent });
        assignment.HasIndex(x => new { x.OfficerUserId, x.IsCurrent });
        assignment.HasQueryFilter(x => !x.Complaint.IsDeleted);
        assignment.HasOne(x => x.Complaint).WithMany(x => x.Assignments).HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Cascade);

        var history = builder.Entity<ComplaintStatusHistory>();
        history.Property(x => x.PreviousStatus).HasConversion<string>().HasMaxLength(40);
        history.Property(x => x.NewStatus).HasConversion<string>().HasMaxLength(40);
        history.Property(x => x.Reason).HasMaxLength(1_000);
        history.Property(x => x.MetadataJson).HasColumnType("jsonb");
        history.HasIndex(x => new { x.ComplaintId, x.CreatedAt });
        history.HasQueryFilter(x => !x.Complaint.IsDeleted);
        history.HasOne(x => x.Complaint).WithMany(x => x.StatusHistory).HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Cascade);

        var response = builder.Entity<ComplaintResponse>();
        response.Property(x => x.Body).HasMaxLength(10_000);
        response.Property(x => x.ResponseSignatureFilePath).HasMaxLength(1_000);
        response.Property(x => x.ResponseInitials).HasMaxLength(6);
        response.Property(x => x.SignedByOfficerName).HasMaxLength(170);
        response.Property(x => x.SignedByAdministrativeUnit).HasMaxLength(150);
        response.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(16);
        response.HasIndex(x => new { x.ComplaintId, x.CreatedAt });
        response.HasQueryFilter(x => !x.Complaint.IsDeleted);
        response.HasOne(x => x.Complaint).WithMany(x => x.Responses).HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Cascade);

        var note = builder.Entity<InternalNote>();
        note.Property(x => x.Body).HasMaxLength(10_000);
        note.HasIndex(x => new { x.ComplaintId, x.CreatedAt });
        note.HasQueryFilter(x => !x.Complaint.IsDeleted);
        note.HasOne(x => x.Complaint).WithMany(x => x.InternalNotes).HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Cascade);

        var evidence = builder.Entity<EvidenceFile>();
        evidence.Property(x => x.OriginalFileName).HasMaxLength(255);
        evidence.Property(x => x.StoredFileName).HasMaxLength(100);
        evidence.Property(x => x.StoragePath).HasMaxLength(1_000);
        evidence.Property(x => x.Extension).HasMaxLength(16);
        evidence.Property(x => x.MimeType).HasMaxLength(128);
        evidence.Property(x => x.Sha256Hash).HasMaxLength(64);
        evidence.Property(x => x.MalwareScanStatus).HasConversion<string>().HasMaxLength(16);
        evidence.HasIndex(x => x.Sha256Hash);
        evidence.HasOne(x => x.Complaint).WithMany(x => x.EvidenceFiles).HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Cascade);
        evidence.HasOne(x => x.Response).WithMany().HasForeignKey(x => x.ResponseId).OnDelete(DeleteBehavior.SetNull);
        evidence.HasQueryFilter(x => !x.IsDeleted && !x.Complaint.IsDeleted);

        var access = builder.Entity<EvidenceAccessLog>();
        access.Property(x => x.AccessType).HasConversion<string>().HasMaxLength(16);
        access.Property(x => x.IpAddress).HasMaxLength(64);
        access.Property(x => x.UserAgent).HasMaxLength(512);
        access.HasIndex(x => new { x.EvidenceFileId, x.CreatedAt });
        access.HasQueryFilter(x => !x.EvidenceFile.IsDeleted && !x.EvidenceFile.Complaint.IsDeleted);
        access.HasOne(x => x.EvidenceFile).WithMany().HasForeignKey(x => x.EvidenceFileId).OnDelete(DeleteBehavior.Restrict);

        var tracking = builder.Entity<AnonymousTrackingRecord>();
        tracking.Property(x => x.TrackingCodeHash).HasMaxLength(64);
        tracking.HasIndex(x => x.ComplaintId).IsUnique();
        tracking.HasQueryFilter(x => !x.Complaint.IsDeleted);
        tracking.HasOne(x => x.Complaint).WithOne().HasForeignKey<AnonymousTrackingRecord>(x => x.ComplaintId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSecurityAndOperations(ModelBuilder builder)
    {
        var notification = builder.Entity<Notification>();
        notification.Property(x => x.Title).HasMaxLength(180);
        notification.Property(x => x.Message).HasMaxLength(4_000);
        notification.Property(x => x.Link).HasMaxLength(500);
        notification.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        notification.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });

        var audit = builder.Entity<AuditLog>();
        audit.Property(x => x.Action).HasMaxLength(100);
        audit.Property(x => x.RelatedRecordType).HasMaxLength(100);
        audit.Property(x => x.RelatedRecordId).HasMaxLength(100);
        audit.Property(x => x.IpAddress).HasMaxLength(64);
        audit.Property(x => x.UserAgent).HasMaxLength(512);
        audit.Property(x => x.MetadataJson).HasColumnType("jsonb");
        audit.HasIndex(x => x.CreatedAt);
        audit.HasIndex(x => new { x.UserId, x.CreatedAt });
        audit.HasIndex(x => new { x.Action, x.CreatedAt });

        var permission = builder.Entity<RolePermission>();
        permission.Property(x => x.Permission).HasMaxLength(100);
        permission.HasIndex(x => new { x.RoleId, x.Permission }).IsUnique();

        var setting = builder.Entity<SystemSetting>();
        setting.Property(x => x.Key).HasMaxLength(150);
        setting.Property(x => x.Value).HasMaxLength(4_000);
        setting.Property(x => x.Description).HasMaxLength(1_000);
        setting.HasIndex(x => x.Key).IsUnique();

        var backup = builder.Entity<BackupRecord>();
        backup.Property(x => x.Reference).HasMaxLength(100);
        backup.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        backup.Property(x => x.StorageReference).HasMaxLength(1_000);
        backup.Property(x => x.RestoreReason).HasMaxLength(1_000);
        backup.Property(x => x.FailureMessage).HasMaxLength(2_000);
        backup.HasIndex(x => x.Reference).IsUnique();

        var session = builder.Entity<ActiveSession>();
        session.Property(x => x.SessionTokenHash).HasMaxLength(64);
        session.Property(x => x.IpAddress).HasMaxLength(64);
        session.Property(x => x.UserAgent).HasMaxLength(512);
        session.Property(x => x.RevocationReason).HasMaxLength(500);
        session.HasIndex(x => x.SessionTokenHash).IsUnique();
        session.HasIndex(x => new { x.UserId, x.RevokedAt, x.ExpiresAt });

        var recovery = builder.Entity<StudentPasswordRecovery>();
        recovery.Property(x => x.RecoveryMethod).HasMaxLength(16);
        recovery.Property(x => x.OtpHash).HasMaxLength(200);
        recovery.Property(x => x.VerificationTokenHash).HasMaxLength(64);
        recovery.HasIndex(x => new { x.UserId, x.CreatedAt });
        recovery.HasIndex(x => x.VerificationTokenHash);

        var verification = builder.Entity<StudentVerification>();
        verification.Property(x => x.Method).HasMaxLength(16);
        verification.Property(x => x.Destination).HasMaxLength(254);
        verification.Property(x => x.CodeHash).HasMaxLength(200);
        verification.HasIndex(x => new { x.StudentId, x.CreatedAt });
    }
}
