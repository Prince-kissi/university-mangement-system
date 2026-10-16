namespace ComplaintSystem.Infrastructure.Options;

public sealed class SmsOptions
{
    public const string Section = "Sms";
    public string Endpoint { get; set; } = "https://sms.arkesel.com/api/v2/sms/send";
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = "UMaT";
    public bool Sandbox { get; set; }
}
