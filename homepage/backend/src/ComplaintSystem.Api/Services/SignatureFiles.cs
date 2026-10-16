namespace ComplaintSystem.Api.Services;

public static class SignatureFiles
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static string? DetectExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        return null;
    }
}
