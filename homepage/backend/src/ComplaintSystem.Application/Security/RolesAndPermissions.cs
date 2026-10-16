namespace ComplaintSystem.Application.Security;

public static class AppRoles
{
    public const string Student = "Student";
    public const string DepartmentOfficer = "DepartmentOfficer";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public static readonly string[] All = [Student, DepartmentOfficer, Admin, SuperAdmin];
}

public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string AdminsManage = "admins.manage";
    public const string RolesManage = "roles.manage";
    public const string DepartmentsManage = "departments.manage";
    public const string CategoriesManage = "categories.manage";
    public const string ComplaintsAssign = "complaints.assign";
    public const string ComplaintsRespond = "complaints.respond";
    public const string ComplaintsInternalNotes = "complaints.internal-notes";
    public const string ComplaintsAllDepartments = "complaints.all-departments";
    public const string EvidenceManage = "evidence.manage";
    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";
    public const string AuditRead = "audit.read";
    public const string SettingsManage = "settings.manage";
    public const string BackupsManage = "backups.manage";
    public const string SessionsManage = "sessions.manage";
    public const string AnnouncementsManage = "announcements.manage";

    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
    {
        [AppRoles.Student] = [],
        [AppRoles.DepartmentOfficer] = [ComplaintsRespond, ComplaintsInternalNotes, EvidenceManage, ReportsView, AnnouncementsManage],
        [AppRoles.Admin] = [UsersRead, UsersManage, DepartmentsManage, CategoriesManage, ComplaintsAssign,
            ComplaintsRespond, ComplaintsInternalNotes, ComplaintsAllDepartments, EvidenceManage,
            ReportsView, ReportsExport, AuditRead, AnnouncementsManage],
        [AppRoles.SuperAdmin] = [UsersRead, UsersManage, AdminsManage, RolesManage, DepartmentsManage,
            CategoriesManage, ComplaintsAssign, ComplaintsRespond, ComplaintsInternalNotes,
            ComplaintsAllDepartments, EvidenceManage, ReportsView, ReportsExport, AuditRead,
            SettingsManage, BackupsManage, SessionsManage, AnnouncementsManage]
    };

    public static string Policy(string permission) => $"Permission:{permission}";
}

public static class ClaimNames
{
    public const string SessionId = "session_id";
    public const string DepartmentId = "department_id";
    public const string AdministrativeUnitId = "administrative_unit_id";
    public const string AdministrativeUnitName = "administrative_unit_name";
    public const string ActualRole = "actual_role";
    public const string ActiveRole = "active_role";
    public const string StudentId = "student_id";
}
