using System.Text.RegularExpressions;
using ComplaintSystem.Api.Services;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Security;
using ComplaintSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize(Policy = "ActiveRole:DepartmentOfficer")]
[Route("api/officer/signature")]
public sealed partial class OfficerSignaturesController(
    UserManager<ApplicationUser> userManager, IFileStorage files, IAuditService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OfficerSignatureDto>> Get()
    {
        var user = await CurrentOfficer();
        return Ok(new OfficerSignatureDto(user.Initials, user.SignatureFilePath is not null, user.SignatureUpdatedAt));
    }

    [HttpGet("image")]
    public async Task<IActionResult> Image(CancellationToken cancellationToken)
    {
        var user = await CurrentOfficer();
        if (user.SignatureFilePath is null) return NotFound();
        return File(await files.OpenReadAsync(user.SignatureFilePath, cancellationToken),
            Path.GetExtension(user.SignatureFilePath).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
    }

    [HttpPut]
    [RequestSizeLimit(SignatureFiles.MaxBytes + 64_000)]
    public async Task<IActionResult> Save([FromForm] string initials, IFormFile? signature, CancellationToken cancellationToken)
    {
        var user = await CurrentOfficer();
        var normalized = initials.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 6 || !InitialsPattern().IsMatch(normalized))
            return BadRequest(new ProblemDetails { Detail = "Initials must be 2 to 6 letters with optional full stops." });
        string? newPath = null;
        if (signature is not null)
        {
            if (signature.Length is <= 0 or > SignatureFiles.MaxBytes)
                return BadRequest(new ProblemDetails { Detail = "The signature must not exceed 2 MB." });
            await using var input = signature.OpenReadStream();
            using var memory = new MemoryStream();
            await input.CopyToAsync(memory, cancellationToken);
            var extension = SignatureFiles.DetectExtension(memory.ToArray());
            if (extension is null)
                return BadRequest(new ProblemDetails { Detail = "Only genuine PNG, JPG, or JPEG signature images are accepted." });
            memory.Position = 0;
            newPath = await files.SaveAsync($"signature-{Guid.NewGuid():N}{extension}", memory, cancellationToken);
        }
        var oldPath = user.SignatureFilePath;
        var oldInitials = user.Initials;
        if (newPath is not null) user.SignatureFilePath = newPath;
        user.Initials = normalized;
        user.SignatureUpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
        if (newPath is not null && oldPath is not null) await files.DeleteAsync(oldPath, cancellationToken);
        if (newPath is not null)
            await audit.WriteAsync(user.Id, oldPath is null ? "SignatureUploaded" : "SignatureReplaced",
                "ApplicationUser", user.Id.ToString(), true, RequestMetadata,
                new { officerId = user.Id, administrativeUnitId = user.DepartmentId }, cancellationToken);
        if (oldInitials != normalized)
            await audit.WriteAsync(user.Id, "InitialsUpdated", "ApplicationUser", user.Id.ToString(), true,
                RequestMetadata, new { officerId = user.Id, administrativeUnitId = user.DepartmentId, oldInitials, newInitials = normalized }, cancellationToken);
        return NoContent();
    }

    [HttpDelete("image")]
    public async Task<IActionResult> Remove(CancellationToken cancellationToken)
    {
        var user = await CurrentOfficer();
        if (user.SignatureFilePath is not null)
        {
            var path = user.SignatureFilePath;
            user.SignatureFilePath = null;
            user.SignatureUpdatedAt = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(user);
            await files.DeleteAsync(path, cancellationToken);
            await audit.WriteAsync(user.Id, "SignatureRemoved", "ApplicationUser", user.Id.ToString(), true,
                RequestMetadata, new { officerId = user.Id, administrativeUnitId = user.DepartmentId }, cancellationToken);
        }
        return NoContent();
    }

    private async Task<ApplicationUser> CurrentOfficer() =>
        await userManager.FindByIdAsync(CurrentUserId.ToString()) ?? throw new ForbiddenException("Officer not found.");

    [GeneratedRegex(@"^(?:[A-Z]\.?){2,6}$")]
    private static partial Regex InitialsPattern();
}

public sealed record OfficerSignatureDto(string? Initials, bool HasSignature, DateTimeOffset? UpdatedAt);
