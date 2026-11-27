using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaVision.Api.Services;
using PharmaVision.Api.Services.Reports;
using PharmaVision.Infrastructure;

namespace PharmaVision.Api.Controllers;

/// <summary>Rapports téléchargeables ; format = pdf | xlsx, period = live | today | 7d | 30d.</summary>
[ApiController]
[Route("api/reports")]
public class ReportsController(ReportBuilder reports, PharmaDbContext db) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Marques du catalogue, pour le rapport laboratoire.</summary>
    [HttpGet("brands")]
    public Task<List<string>> Brands(CancellationToken ct) =>
        db.Products.Where(p => p.Brand != null).Select(p => p.Brand!).Distinct().OrderBy(b => b).ToListAsync(ct);

    [HttpGet("{kind}")]
    public async Task<IActionResult> Download(
        string kind, int pharmacyId, string? period, string? brand, CancellationToken ct, string format = "pdf")
    {
        if (!Enum.TryParse<ReportKind>(kind, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            return NotFound("Rapport inconnu");
        if (format is not ("pdf" or "xlsx")) return BadRequest("Format attendu : pdf ou xlsx");
        if (type == ReportKind.Laboratoire && string.IsNullOrWhiteSpace(brand)) return BadRequest("Choisissez une marque");

        var report = await reports.BuildAsync(type, pharmacyId, PeriodRange.Parse(period), brand, ct);
        if (report is null) return NotFound("Officine ou marque inconnue");

        var city = await db.Pharmacies.Where(p => p.Id == pharmacyId).Select(p => p.City).FirstAsync(ct);
        var subject = type == ReportKind.Laboratoire ? $"laboratoire-{brand}" : type.ToString();
        var name = $"pharmavision-{Slug(subject)}-{Slug(city)}-{DateTime.Now:yyyy-MM-dd}.{format}";

        return format == "pdf"
            ? File(PdfRenderer.Render(report), "application/pdf", name)
            : File(ExcelRenderer.Render(report), Xlsx, name);
    }

    /// <summary>Nom de fichier sans accents ni caractères spéciaux.</summary>
    private static string Slug(string value)
    {
        var plain = value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        return string.Join('-', new string(plain.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
