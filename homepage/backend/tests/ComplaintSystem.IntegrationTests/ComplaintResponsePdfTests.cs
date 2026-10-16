using ComplaintSystem.Api.Services;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.IntegrationTests;

public sealed class ComplaintResponsePdfTests
{
    [Fact]
    public void CreatesReadableMultipageReadyResponseDocument()
    {
        var complaint = new ComplaintDetailsDto(
            Guid.NewGuid(), "UMAT-2026-DEMO1234", "Delay in examination results",
            "The student requested clarification about a delayed result.",
            "Academic", "Academic Affairs / Examinations", PriorityLevel.Normal,
            ComplaintStatus.Resolved, false, DateTimeOffset.Parse("2026-07-20T09:00:00Z"),
            null, DateTimeOffset.Parse("2026-07-25T09:00:00Z"), [],
            [
                new ResponseDto(Guid.NewGuid(),
                    "Your examination result has been verified and published. The delay was caused by a final moderation review. Please sign in to the student portal to confirm the updated grade.",
                    true, DateTimeOffset.Parse("2026-07-24T14:30:00Z"))
            ], []);

        var pdf = ComplaintResponsePdf.Create(complaint);

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Contains("UMaT Complaint Response", System.Text.Encoding.ASCII.GetString(pdf));

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var output = Path.Combine(root, "output", "pdf");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "complaint-response-preview.pdf"), pdf);
    }
}
