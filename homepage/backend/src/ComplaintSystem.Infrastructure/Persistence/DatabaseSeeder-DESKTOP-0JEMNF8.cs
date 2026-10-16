using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Persistence;

public sealed class DatabaseSeeder(
    ApplicationDbContext dbContext,
    RoleManager<IdentityRole<Guid>> roleManager)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAndPermissionsAsync(cancellationToken);
        await SeedDepartmentsAndCategoriesAsync(cancellationToken);
        await SeedSystemSettingsAsync(cancellationToken);
    }

    private async Task SeedSystemSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = new[]
        {
            new SystemSetting { Key = "AnonymousComplaints.Enabled", Value = "true", Description = "Allow anonymous complaint submission." },
            new SystemSetting { Key = "Notifications.EmailEnabled", Value = "true", Description = "Send configured email notifications." },
            new SystemSetting { Key = "Registration.StudentEmailDomain", Value = "st.umat.edu.gh", Description = "Approved domain for student registration verification." },
            new SystemSetting { Key = "Complaints.AutoEscalationEnabled", Value = "true", Description = "Escalate complaints after their resolution deadline." },
            new SystemSetting { Key = "Security.SessionMinutes", Value = "30", Description = "Interactive session duration in minutes." },
            new SystemSetting { Key = "Evidence.RetentionDays", Value = "2555", Description = "Evidence retention period in days." }
        };
        foreach (var setting in settings)
        {
            if (!await dbContext.SystemSettings.AnyAsync(x => x.Key == setting.Key, cancellationToken))
                dbContext.SystemSettings.Add(setting);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedRolesAndPermissionsAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in AppRoles.All)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() };
                var result = await roleManager.CreateAsync(role);
                EnsureSucceeded(result, $"create role {roleName}");
            }
            if (!Permissions.Defaults.TryGetValue(roleName, out var permissions))
            {
                continue;
            }
            foreach (var permission in permissions)
            {
                if (!await dbContext.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.Permission == permission, cancellationToken))
                {
                    dbContext.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        Permission = permission,
                        GrantedByUserId = Guid.Empty
                    });
                }
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedDepartmentsAndCategoriesAsync(CancellationToken cancellationToken)
    {
        var departmentSeeds = new[]
        {
            ("REG", "Registry"),
            ("ICT", "ICT Directorate"),
            ("FIN", "Finance Office"),
            ("LIB", "University Library"),
            ("SA", "Student Affairs"),
            ("HHA", "Hall/Hostel Administration"),
            ("SEC", "Security Unit"),
            ("EST", "Works and Physical Development"),
            ("TRN", "Transport Unit"),
            ("HR", "Human Resource Unit"),
            ("HLT", "Health Services"),
            ("PRO", "Procurement Unit"),
            ("AA", "Academic Affairs / Examinations"),
            ("COU", "Counselling Unit"),
            ("QA", "Quality Assurance Unit")
        };
        foreach (var (code, name) in departmentSeeds)
        {
            var department = await dbContext.Departments.SingleOrDefaultAsync(x => x.Code == code, cancellationToken);
            if (department is null)
            {
                dbContext.Departments.Add(new Department { Code = code, Name = name, IsActive = true });
            }
            else
            {
                department.Name = name;
                department.IsActive = true;
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var academic = await dbContext.Departments.SingleAsync(x => x.Code == "AA", cancellationToken);
        var student = await dbContext.Departments.SingleAsync(x => x.Code == "SA", cancellationToken);
        var ict = await dbContext.Departments.SingleAsync(x => x.Code == "ICT", cancellationToken);
        var categories = new[]
        {
            new ComplaintCategory { Name = "Academic Records", Description = "Results, registration and academic records.", DepartmentId = academic.Id, DefaultPriority = PriorityLevel.Normal },
            new ComplaintCategory { Name = "Teaching and Learning", Description = "Teaching delivery, timetables and learning support.", DepartmentId = academic.Id, DefaultPriority = PriorityLevel.Normal },
            new ComplaintCategory { Name = "Student Welfare", Description = "Accommodation, wellbeing and student services.", DepartmentId = student.Id, DefaultPriority = PriorityLevel.High },
            new ComplaintCategory { Name = "IT Services", Description = "University accounts, networks and digital systems.", DepartmentId = ict.Id, DefaultPriority = PriorityLevel.Normal },
            new ComplaintCategory { Name = "Safety Concern", Description = "Urgent campus health or safety concerns.", DepartmentId = student.Id, DefaultPriority = PriorityLevel.Urgent }
        };
        foreach (var category in categories)
        {
            if (!await dbContext.ComplaintCategories.AnyAsync(x => x.Name == category.Name && x.DepartmentId == category.DepartmentId, cancellationToken))
            {
                dbContext.ComplaintCategories.Add(category);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Failed to {operation}: {string.Join(", ", result.Errors.Select(x => x.Code))}");
        }
    }
}
