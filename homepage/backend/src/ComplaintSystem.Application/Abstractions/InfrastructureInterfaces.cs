namespace ComplaintSystem.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IEmailSender
{
    Task SendVerificationAsync(string email, string name, string verificationUrl, CancellationToken cancellationToken);
    Task SendPasswordResetAsync(string email, string name, string resetUrl, CancellationToken cancellationToken);
    Task SendPasswordRecoveryCodeAsync(string email, string name, string code, CancellationToken cancellationToken);
    Task SendTemporaryPasswordAsync(string email, string name, string temporaryPassword, CancellationToken cancellationToken);
}

public interface ISmsSender
{
    bool IsConfigured { get; }
    Task SendPasswordRecoveryCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken);
}

public interface IFileStorage
{
    Task<string> SaveAsync(string generatedFileName, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken);
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken);
}

public sealed record MalwareScanResult(bool IsClean, string? Detail);
public interface IMalwareScanner
{
    Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}

public interface IAuditService
{
    Task WriteAsync(Guid? userId, string action, string recordType, string? recordId, bool succeeded,
        RequestMetadata metadata, object? structuredMetadata, CancellationToken cancellationToken);
}

public interface ISessionService
{
    Task<string> CreateAsync(Guid userId, RequestMetadata metadata, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<bool> IsActiveAsync(string sessionId, CancellationToken cancellationToken);
    Task TouchAsync(string sessionId, CancellationToken cancellationToken);
    Task RevokeAsync(string sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken);
    Task RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken);
}

public interface IAnonymousTrackingService
{
    string GenerateCode();
    string HashCode(string code);
    bool Verify(string code, string expectedHash);
}

public interface IPermissionService
{
    Task<bool> UserHasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken);
}

public interface IBackupProvider
{
    Task<(string StorageReference, long SizeBytes)> CreateAsync(string reference, CancellationToken cancellationToken);
    Task RestoreAsync(string storageReference, CancellationToken cancellationToken);
}
