using ComplaintSystem.Application.Abstractions;

namespace ComplaintSystem.IntegrationTests;

public sealed class FakeEmailSender : IEmailSender
{
    public string? LastVerificationUrl { get; private set; }
    public string? LastResetUrl { get; private set; }
    public string? LastRecoveryCode { get; private set; }

    public Task SendVerificationAsync(string email, string name, string verificationUrl, CancellationToken cancellationToken)
    {
        LastVerificationUrl = verificationUrl;
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string name, string resetUrl, CancellationToken cancellationToken)
    {
        LastResetUrl = resetUrl;
        return Task.CompletedTask;
    }

    public Task SendTemporaryPasswordAsync(string email, string name, string temporaryPassword, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SendPasswordRecoveryCodeAsync(string email, string name, string code, CancellationToken cancellationToken)
    {
        LastRecoveryCode = code;
        return Task.CompletedTask;
    }
}
