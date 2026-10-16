using ComplaintSystem.Api.Services;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ComplaintSystem.Api.Controllers;

[Authorize]
[Route("api/profile")]
public sealed class ProfileController(
    UserManager<ApplicationUser> userManager, IFileStorage files, IAuditService audit) : ApiControllerBase
{
    private const long MaximumPhotoBytes = 2 * 1024 * 1024;

    [HttpGet("photo")]
    public async Task<IActionResult> Photo(CancellationToken cancellationToken)
    {
        var user = await CurrentUser();
        if (string.IsNullOrWhiteSpace(user.ProfileImageUrl)) return NotFound();
        var extension = Path.GetExtension(user.ProfileImageUrl);
        var contentType = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return File(await files.OpenReadAsync(user.ProfileImageUrl, cancellationToken), contentType);
    }

    [HttpPut("photo")]
    [RequestSizeLimit(MaximumPhotoBytes + 64_000)]
    public async Task<IActionResult> SavePhoto(IFormFile photo, CancellationToken cancellationToken)
    {
        if (photo.Length is <= 0 or > MaximumPhotoBytes)
            return BadRequest(new ProblemDetails { Detail = "The profile photo must not exceed 2 MB." });
        await using var input = photo.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, cancellationToken);
        var extension = DetectImageExtension(memory.ToArray());
        if (extension is null)
            return BadRequest(new ProblemDetails { Detail = "Only genuine PNG, JPG, or JPEG profile photos are accepted." });
        memory.Position = 0;
        var newPath = await files.SaveAsync($"profile-{Guid.NewGuid():N}{extension}", memory, cancellationToken);
        var user = await CurrentUser();
        var oldPath = user.ProfileImageUrl;
        user.ProfileImageUrl = newPath;
        await userManager.UpdateAsync(user);
        if (!string.IsNullOrWhiteSpace(oldPath)) await files.DeleteAsync(oldPath, cancellationToken);
        await audit.WriteAsync(user.Id, "ProfilePhotoUpdated", "ApplicationUser", user.Id.ToString(), true,
            RequestMetadata, new { }, cancellationToken);
        return NoContent();
    }

    private async Task<ApplicationUser> CurrentUser() =>
        await userManager.FindByIdAsync(CurrentUserId.ToString()) ?? throw new UnauthorizedAccessException();

    private static string? DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        return null;
    }
}
