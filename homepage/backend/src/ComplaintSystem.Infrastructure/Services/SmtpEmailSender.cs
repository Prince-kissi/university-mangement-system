using System.Net;
using System.Net.Mail;
using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public Task SendVerificationAsync(string email, string name, string verificationUrl, CancellationToken cancellationToken) =>
        SendAsync(email, name, "Verify your UMaT complaint account",
            $"Hello {WebUtility.HtmlEncode(name)},<br><br>Verify your account using this secure link:<br><a href=\"{WebUtility.HtmlEncode(verificationUrl)}\">Verify email</a>.", cancellationToken);

    public Task SendPasswordResetAsync(string email, string name, string resetUrl, CancellationToken cancellationToken) =>
        SendAsync(email, name, "Reset your UMaT complaint account password",
            $"Hello {WebUtility.HtmlEncode(name)},<br><br>Reset your password using this secure link:<br><a href=\"{WebUtility.HtmlEncode(resetUrl)}\">Reset password</a>. If you did not request this, ignore this email.", cancellationToken);

    public Task SendPasswordRecoveryCodeAsync(string email, string name, string code, CancellationToken cancellationToken) =>
        SendAsync(email, name, "Password Reset Verification Code",
            $"Dear {WebUtility.HtmlEncode(name)},<br><br>Your verification code is:<br><strong style=\"font-size:24px;letter-spacing:5px\">{WebUtility.HtmlEncode(code)}</strong><br><br>The code expires in 5 minutes.<br><br>If you did not request this password reset, please ignore this email.<br><br>UMaT Complaint Management System", cancellationToken);

    public Task SendTemporaryPasswordAsync(string email, string name, string temporaryPassword, CancellationToken cancellationToken) =>
        SendAsync(email, name, "Your UMaT complaint staff account",
            $"Hello {WebUtility.HtmlEncode(name)},<br><br>Your temporary password is <strong>{WebUtility.HtmlEncode(temporaryPassword)}</strong>. You must change it immediately and configure two-factor authentication if required.", cancellationToken);

    private async Task SendAsync(string email, string name, string subject, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            throw new InvalidOperationException("SMTP is not configured.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(email, name));
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseTls,
            Credentials = string.IsNullOrWhiteSpace(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
