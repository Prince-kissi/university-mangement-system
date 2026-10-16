namespace ComplaintSystem.Infrastructure.Options;

public sealed class FrontendOptions
{
    public const string Section = "Frontend";
    public string BaseUrl { get; set; } = "https://localhost";
}

public sealed class SecurityOptions
{
    public const string Section = "Security";
    public string AnonymousTrackingPepper { get; set; } = string.Empty;
    public int SessionMinutes { get; set; } = 30;
    public int RememberMeHours { get; set; } = 8;
    public int AnonymousTrackingLockoutMinutes { get; set; } = 15;
    public int AnonymousTrackingMaxFailures { get; set; } = 5;
}

public sealed class EvidenceOptions
{
    public const string Section = "Evidence";
    public string StoragePath { get; set; } = "storage/evidence";
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}

public sealed class BootstrapOptions
{
    public const string Section = "Bootstrap";
    public string? SuperAdminEmail { get; set; }
    public string? SuperAdminPassword { get; set; }
}

public sealed class BackupOptions
{
    public const string Section = "Backups";
    public string StoragePath { get; set; } = "storage/backups";
    public string? Command { get; set; }
    public string? RestoreCommand { get; set; }
}

public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "no-reply@umat.edu.gh";
    public string FromName { get; set; } = "UMaT Complaint System";
}

public sealed class SmsOptions
{
    public const string Section = "Sms";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = "UMaT CMS";
}
