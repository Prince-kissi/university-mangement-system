using System.Net.Http.Json;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class HttpSmsSender(HttpClient client, IOptions<SmsOptions> options) : ISmsSender
{
    private readonly SmsOptions _options = options.Value;
    public bool IsConfigured => Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task SendPasswordRecoveryCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("SMS delivery is not configured.");
        var recipient = NormalizeGhanaPhoneNumber(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                sender = _options.SenderId,
                message = $"Your UMaT verification code is {code}. It expires in 5 minutes. Do not share this code.",
                recipients = new[] { recipient },
                sandbox = _options.Sandbox
            })
        };
        request.Headers.Add("api-key", _options.ApiKey);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static string NormalizeGhanaPhoneNumber(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0", StringComparison.Ordinal) && digits.Length == 10)
            return $"233{digits[1..]}";
        if (digits.StartsWith("233", StringComparison.Ordinal) && digits.Length == 12)
            return digits;
        throw new ArgumentException("A valid Ghana phone number is required.", nameof(phoneNumber));
    }
}
