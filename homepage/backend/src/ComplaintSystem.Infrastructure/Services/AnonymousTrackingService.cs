using System.Security.Cryptography;
using System.Text;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class AnonymousTrackingService(IOptions<SecurityOptions> options) : IAnonymousTrackingService
{
    private readonly byte[] _pepper = Encoding.UTF8.GetBytes(options.Value.AnonymousTrackingPepper);

    public string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public string HashCode(string code)
    {
        if (_pepper.Length < 32)
        {
            throw new InvalidOperationException("Security:AnonymousTrackingPepper must contain at least 32 characters.");
        }
        return Convert.ToHexString(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(code.Trim())));
    }

    public bool Verify(string code, string expectedHash)
    {
        var actual = Convert.FromHexString(HashCode(code));
        var expected = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
