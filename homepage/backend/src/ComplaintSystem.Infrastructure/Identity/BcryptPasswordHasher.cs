using Microsoft.AspNetCore.Identity;

namespace ComplaintSystem.Infrastructure.Identity;

public sealed class BcryptPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private const int WorkFactor = 12;
    private readonly PasswordHasher<ApplicationUser> _legacyHasher = new();

    public string HashPassword(ApplicationUser user, string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword,
        string providedPassword)
    {
        if (hashedPassword.StartsWith("$2", StringComparison.Ordinal))
        {
            return BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }

        var legacyResult = _legacyHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
        return legacyResult == PasswordVerificationResult.Failed
            ? PasswordVerificationResult.Failed
            : PasswordVerificationResult.SuccessRehashNeeded;
    }
}
