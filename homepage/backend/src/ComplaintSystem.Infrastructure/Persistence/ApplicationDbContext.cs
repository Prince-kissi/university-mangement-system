using ComplaintSystem.Domain.Common;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ComplaintCategory> ComplaintCategories => Set<ComplaintCategory>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ComplaintAssignment> ComplaintAssignments => Set<ComplaintAssignment>();
    public DbSet<ComplaintStatusHistory> ComplaintStatusHistory => Set<ComplaintStatusHistory>();
    public DbSet<ComplaintResponse> ComplaintResponses => Set<ComplaintResponse>();
    public DbSet<InternalNote> InternalNotes => Set<InternalNote>();
    public DbSet<EvidenceFile> EvidenceFiles => Set<EvidenceFile>();
    public DbSet<EvidenceAccessLog> EvidenceAccessLogs => Set<EvidenceAccessLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AnonymousTrackingRecord> AnonymousTrackingRecords => Set<AnonymousTrackingRecord>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<ActiveSession> ActiveSessions => Set<ActiveSession>();
    public DbSet<StudentPasswordRecovery> StudentPasswordRecoveries => Set<StudentPasswordRecovery>();
    public DbSet<StudentVerification> StudentVerifications => Set<StudentVerification>();
    public DbSet<Student> Students => Set<Student>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ModelConfiguration.Configure(builder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyPersistenceRules();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyPersistenceRules()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.Entity is AuditLog or ComplaintSystem.Domain.Entities.ComplaintStatusHistory or EvidenceAccessLog &&
                entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} records are immutable.");
            }

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
                entry.Entity.ConcurrencyToken = Guid.NewGuid();
            }
        }

        foreach (var entry in ChangeTracker.Entries<ApplicationUser>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
                entry.Entity.ConcurrencyToken = Guid.NewGuid();
            }
        }

        foreach (var entry in ChangeTracker.Entries<Student>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.Email = entry.Entity.Email.Trim().ToLowerInvariant();
        }

        foreach (var entry in ChangeTracker.Entries<SoftDeletableEntity>().Where(x => x.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
        }
    }
}
