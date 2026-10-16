using System.IO.Compression;
using System.Security.Cryptography;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Domain.Enums;
using ComplaintSystem.Infrastructure.Identity;
using ComplaintSystem.Infrastructure.Options;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class EvidenceService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IAnonymousTrackingService trackingService,
    IPermissionService permissionService,
    ICurrentUser currentUser,
    IFileStorage fileStorage,
    IMalwareScanner malwareScanner,
    IAuditService auditService,
    IOptions<EvidenceOptions> options) : IEvidenceService
{
    private readonly EvidenceOptions _options = options.Value;
    private static readonly IReadOnlyDictionary<string, string> AllowedMimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

    public async Task<Result<EvidenceDto>> UploadAsync(Guid? userId, Guid complaintId, EvidenceUpload upload,
        string? anonymousTrackingCode, CancellationToken cancellationToken)
    {
        var complaint = await dbContext.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId, cancellationToken)
            ?? throw new NotFoundException("Complaint not found.");
        await EnsureUploadAccessAsync(userId, complaint, anonymousTrackingCode, cancellationToken);
        if (upload.Length <= 0 || upload.Length > _options.MaxFileSizeBytes)
        {
            return Result<EvidenceDto>.Failure("invalid_file_size", $"Files must be between 1 byte and {_options.MaxFileSizeBytes} bytes.");
        }
        var originalName = Path.GetFileName(upload.FileName.Trim());
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Any(char.IsControl))
        {
            return Result<EvidenceDto>.Failure("invalid_file_name", "The file name is invalid.");
        }
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (!AllowedMimeTypes.TryGetValue(extension, out var expectedMime))
        {
            return Result<EvidenceDto>.Failure("file_type_not_allowed", "Only PDF, PNG, JPEG, WebP and DOCX files are accepted.");
        }
        var suppliedMime = upload.ContentType.Split(';', 2)[0].Trim();
        if (!string.Equals(suppliedMime, expectedMime, StringComparison.OrdinalIgnoreCase))
        {
            return Result<EvidenceDto>.Failure("mime_type_mismatch", "The declared file type does not match its extension.");
        }

        await using var buffer = new MemoryStream();
        await CopyWithLimitAsync(upload.Content, buffer, _options.MaxFileSizeBytes, cancellationToken);
        if (buffer.Length != upload.Length)
        {
            return Result<EvidenceDto>.Failure("file_length_mismatch", "The uploaded file length is inconsistent.");
        }
        buffer.Position = 0;
        if (!HasValidSignature(extension, buffer))
        {
            return Result<EvidenceDto>.Failure("file_signature_mismatch", "The file content does not match its extension.");
        }
        buffer.Position = 0;
        var scan = await malwareScanner.ScanAsync(buffer, cancellationToken);
        if (!scan.IsClean)
        {
            return Result<EvidenceDto>.Failure("malware_detected", "The file did not pass the security scan.");
        }
        buffer.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(buffer, cancellationToken));
        buffer.Position = 0;
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var path = await fileStorage.SaveAsync(storedName, buffer, cancellationToken);
        var evidence = new EvidenceFile
        {
            ComplaintId = complaint.Id,
            UploadedByUserId = userId,
            OriginalFileName = originalName,
            StoredFileName = storedName,
            StoragePath = path,
            Extension = extension,
            MimeType = expectedMime,
            SizeBytes = buffer.Length,
            Sha256Hash = hash,
            MalwareScanStatus = MalwareScanStatus.Clean
        };
        try
        {
            dbContext.EvidenceFiles.Add(evidence);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(path, CancellationToken.None);
            throw;
        }
        return Result<EvidenceDto>.Success(new EvidenceDto(evidence.Id, evidence.OriginalFileName,
            evidence.MimeType, evidence.SizeBytes, evidence.CreatedAt));
    }

    public async Task<EvidenceDownload> DownloadAsync(Guid userId, Guid evidenceId, EvidenceAccessType accessType,
        RequestMetadata metadata, CancellationToken cancellationToken)
    {
        var evidence = await dbContext.EvidenceFiles.Include(x => x.Complaint)
            .SingleOrDefaultAsync(x => x.Id == evidenceId, cancellationToken)
            ?? throw new NotFoundException("Evidence not found.");
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException("User not found.");
        await EnsureAuthenticatedAccessAsync(user, evidence.Complaint, cancellationToken);
        dbContext.EvidenceAccessLogs.Add(new EvidenceAccessLog
        {
            EvidenceFileId = evidence.Id,
            AccessedByUserId = userId,
            AccessType = accessType,
            IpAddress = metadata.IpAddress,
            UserAgent = metadata.UserAgent
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(userId, "EvidenceAccess", "EvidenceFile", evidence.Id.ToString(), true,
            metadata, new { accessType = accessType.ToString() }, cancellationToken);
        return new EvidenceDownload(evidence.OriginalFileName, evidence.MimeType,
            await fileStorage.OpenReadAsync(evidence.StoragePath, cancellationToken));
    }

    public async Task<Result> SoftDeleteAsync(Guid actorId, Guid evidenceId, CancellationToken cancellationToken)
    {
        var evidence = await dbContext.EvidenceFiles.Include(x => x.Complaint)
            .SingleOrDefaultAsync(x => x.Id == evidenceId, cancellationToken)
            ?? throw new NotFoundException("Evidence not found.");
        var actor = await userManager.FindByIdAsync(actorId.ToString()) ?? throw new ForbiddenException("User not found.");
        if (await userManager.IsInRoleAsync(actor, AppRoles.Student))
        {
            throw new ForbiddenException("Students cannot delete submitted evidence.");
        }
        await EnsureAuthenticatedAccessAsync(actor, evidence.Complaint, cancellationToken);
        evidence.IsDeleted = true;
        evidence.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task EnsureUploadAccessAsync(Guid? userId, Complaint complaint, string? trackingCode,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue)
        {
            if (!complaint.IsAnonymous || string.IsNullOrWhiteSpace(trackingCode))
            {
                throw new ForbiddenException("Anonymous tracking credentials are required.");
            }
            var tracking = await dbContext.AnonymousTrackingRecords.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ComplaintId == complaint.Id, cancellationToken);
            if (tracking is null || !trackingService.Verify(trackingCode, tracking.TrackingCodeHash))
            {
                throw new ForbiddenException("Anonymous tracking credentials are invalid.");
            }
            return;
        }
        var user = await userManager.FindByIdAsync(userId.Value.ToString()) ?? throw new ForbiddenException("User not found.");
        if (await userManager.IsInRoleAsync(user, AppRoles.Student))
        {
            if (complaint.StudentUserId != user.Id) throw new ForbiddenException("You cannot add evidence to another student's complaint.");
            return;
        }
        await EnsureAuthenticatedAccessAsync(user, complaint, cancellationToken);
    }

    private async Task EnsureAuthenticatedAccessAsync(ApplicationUser user, Complaint complaint,
        CancellationToken cancellationToken)
    {
        if (currentUser.ActiveRole == AppRoles.DepartmentOfficer)
        {
            if (!currentUser.DepartmentId.HasValue || currentUser.DepartmentId.Value != complaint.DepartmentId)
                throw new ForbiddenException("You cannot access evidence outside your active department.");
            return;
        }
        if (await userManager.IsInRoleAsync(user, AppRoles.Student))
        {
            if (complaint.StudentUserId != user.Id) throw new ForbiddenException("You cannot access this evidence.");
            return;
        }
        if (await permissionService.UserHasPermissionAsync(user.Id, Permissions.ComplaintsAllDepartments, cancellationToken))
        {
            return;
        }
        if (!await userManager.IsInRoleAsync(user, AppRoles.DepartmentOfficer) || user.DepartmentId != complaint.DepartmentId)
        {
            throw new ForbiddenException("You cannot access evidence outside your department.");
        }
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maxBytes) throw new BadHttpRequestException("The uploaded file exceeds the configured limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static bool HasValidSignature(string extension, MemoryStream stream)
    {
        var bytes = stream.ToArray();
        return extension switch
        {
            ".pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
            ".png" => bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".docx" => IsValidDocx(stream),
            _ => false
        };
    }

    private static bool IsValidDocx(MemoryStream stream)
    {
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            return archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
