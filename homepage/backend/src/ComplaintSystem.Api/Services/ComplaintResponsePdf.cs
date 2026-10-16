using System.Globalization;
using System.Text;
using ComplaintSystem.Application.Contracts;

namespace ComplaintSystem.Api.Services;

public static class ComplaintResponsePdf
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;

    public static byte[] Create(ComplaintDetailsDto complaint)
    {
        var pages = new List<StringBuilder>();
        var page = NewPage(pages, complaint);
        var y = 704d;

        AddLabel(page, 54, y, "COMPLAINT SUMMARY");
        y -= 28;
        AddField(page, "Reference", complaint.ComplaintId, 54, ref y);
        AddField(page, "Subject", complaint.Title, 54, ref y);
        AddField(page, "Administrative unit", complaint.Department, 54, ref y);
        AddField(page, "Category", complaint.Category, 54, ref y);
        AddField(page, "Status", Humanize(complaint.Status.ToString()), 54, ref y);
        AddField(page, "Submitted", FormatDate(complaint.CreatedAt), 54, ref y);
        y -= 10;

        AddLabel(page, 54, y, "OFFICIAL RESPONSE");
        y -= 28;
        var staffResponses = complaint.Responses.Where(x => x.IsStaffResponse).OrderBy(x => x.CreatedAt).ToList();
        if (staffResponses.Count == 0)
        {
            AddWrapped(page, "No official department response has been issued yet.", 54, ref y, 487, 11, "F1");
        }
        else
        {
            for (var index = 0; index < staffResponses.Count; index++)
            {
                var response = staffResponses[index];
                EnsureSpace(ref page, pages, complaint, ref y, 95);
                page.AppendLine("0.941 0.980 0.961 rg 48 " + F(y - 18) + " 499 34 re f");
                AddText(page, 60, y, "F2", 11, staffResponses.Count == 1 ? "Response from UMaT" : $"Response {index + 1} from UMaT");
                AddText(page, 60, y - 16, "F1", 9, FormatDate(response.CreatedAt));
                y -= 55;
                foreach (var line in Wrap(response.Body, 86))
                {
                    EnsureSpace(ref page, pages, complaint, ref y, 24);
                    AddText(page, 60, y, "F1", 11, line);
                    y -= 16;
                }
                if (!string.IsNullOrWhiteSpace(response.SignedByName))
                {
                    y -= 4;
                    AddText(page, 60, y, "F2", 9, $"Submitted by: {response.SignedByName}");
                    y -= 14;
                    AddText(page, 60, y, "F1", 9, response.AdministrativeUnit ?? complaint.Department);
                    y -= 14;
                    if (!string.IsNullOrWhiteSpace(response.Initials))
                    {
                        AddText(page, 60, y, "F1", 9, $"Initials: {response.Initials}");
                        y -= 14;
                    }
                    if (response.SignatureIncluded)
                    {
                        AddText(page, 60, y, "F1", 8, "A signature image was included with the authenticated response.");
                        y -= 14;
                    }
                }
                y -= 18;
            }
        }

        EnsureSpace(ref page, pages, complaint, ref y, 95);
        page.AppendLine("0.025 0.337 0.247 RG 0.8 w 54 " + F(y) + " 487 0 re S");
        y -= 25;
        AddText(page, 54, y, "F2", 10, "Document verification");
        y -= 17;
        AddWrapped(page,
            $"Generated from the UMaT Complaint Management System on {FormatDate(DateTimeOffset.UtcNow)}. " +
            "This document contains the public response recorded for the complaint above.",
            54, ref y, 487, 9, "F1");

        for (var i = 0; i < pages.Count; i++)
        {
            AddText(pages[i], 54, 35, "F1", 8, "University of Mines and Technology - Complaint Management System");
            AddText(pages[i], 500, 35, "F1", 8, $"{i + 1} / {pages.Count}");
        }
        return BuildDocument(pages);
    }

    private static StringBuilder NewPage(List<StringBuilder> pages, ComplaintDetailsDto complaint)
    {
        var page = new StringBuilder();
        pages.Add(page);
        page.AppendLine("0.025 0.337 0.247 rg 0 762 595 80 re f");
        AddText(page, 54, 806, "F2", 18, "UMaT Complaint Response");
        AddText(page, 54, 784, "F1", 10, $"Official response record - {complaint.ComplaintId}");
        AddText(page, 54, 737, "F2", 15, complaint.Title);
        return page;
    }

    private static void EnsureSpace(ref StringBuilder page, List<StringBuilder> pages,
        ComplaintDetailsDto complaint, ref double y, double required)
    {
        if (y - required >= 65) return;
        page = NewPage(pages, complaint);
        y = 704;
    }

    private static void AddField(StringBuilder page, string label, string value, double x, ref double y)
    {
        AddText(page, x, y, "F2", 9, label.ToUpperInvariant());
        AddText(page, x + 135, y, "F1", 10, value);
        y -= 23;
    }

    private static void AddLabel(StringBuilder page, double x, double y, string text)
    {
        AddText(page, x, y, "F2", 10, text);
        page.AppendLine($"0.055 0.561 0.471 rg {F(x)} {F(y - 7)} 34 2 re f");
    }

    private static void AddWrapped(StringBuilder page, string text, double x, ref double y,
        double width, double size, string font)
    {
        var maxCharacters = Math.Max(20, (int)(width / (size * 0.52)));
        foreach (var line in Wrap(text, maxCharacters))
        {
            AddText(page, x, y, font, size, line);
            y -= size * 1.45;
        }
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        foreach (var paragraph in value.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                yield return "";
                continue;
            }
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0) yield return line.ToString();
        }
    }

    private static void AddText(StringBuilder page, double x, double y, string font, double size, string text) =>
        page.AppendLine($"BT /{font} {F(size)} Tf {F(x)} {F(y)} Td ({Escape(text)}) Tj ET");

    private static string Escape(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKD);
        var ascii = new string(normalized.Where(c => c is >= ' ' and <= '~').ToArray());
        return ascii.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static string Humanize(string value) =>
        string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? $" {c}" : c.ToString()));

    private static string FormatDate(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.Zero).ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static byte[] BuildDocument(IReadOnlyList<StringBuilder> pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{5 + i * 2} 0 R"))}] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
        };
        for (var i = 0; i < pages.Count; i++)
        {
            var content = pages[i].ToString();
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + i * 2} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        using var output = new MemoryStream();
        void Write(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }
}
