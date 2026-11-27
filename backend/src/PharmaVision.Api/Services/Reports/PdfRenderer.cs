using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace PharmaVision.Api.Services.Reports;

public static class PdfRenderer
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Color Brand = new(0x0B, 0x6E, 0x4C);
    private static readonly Color Line = new(0xE1, 0xE8, 0xE5);
    private static readonly Color Muted = new(0x5B, 0x6D, 0x66);
    private const double UsableWidthCm = 26.1; // A4 paysage moins les marges
    private const int FirstColumnWeight = 2;

    static PdfRenderer() => GlobalFontSettings.FontResolver = new ReportFontResolver();

    public static byte[] Render(Report report)
    {
        var document = new Document();
        document.Info.Title = report.Title;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = ReportFontResolver.Family;
        normal.Font.Size = 8.5;

        var page = document.AddSection();
        page.PageSetup = document.DefaultPageSetup.Clone();
        page.PageSetup.PageWidth = Unit.FromCentimeter(29.7);
        page.PageSetup.PageHeight = Unit.FromCentimeter(21);
        page.PageSetup.LeftMargin = page.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        page.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        page.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);

        var footer = page.Footers.Primary.AddParagraph("PharmaVision · page ");
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.Format.Font.Color = Muted;

        var title = page.AddParagraph(report.Title);
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Brand;
        page.AddParagraph(report.Subtitle).Format.SpaceAfter = 6;
        foreach (var note in report.Notes) page.AddParagraph(note).Format.Font.Color = Muted;

        foreach (var section in report.Sections) Add(page, section);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static void Add(Section page, ReportSection section)
    {
        var heading = page.AddParagraph(section.Title);
        heading.Format.Font.Size = 12;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = 14;
        heading.Format.SpaceAfter = 6;
        heading.Format.KeepWithNext = true;

        if (section.Rows.Count == 0)
        {
            page.AddParagraph("Aucune donnée sur la période.").Format.Font.Color = Muted;
            return;
        }

        var table = page.AddTable();
        table.Borders.Bottom.Color = Line;
        table.Borders.Bottom.Width = 0.5;
        table.TopPadding = table.BottomPadding = 2.5;
        var unit = UsableWidthCm / (section.Columns.Count - 1 + FirstColumnWeight);
        for (var c = 0; c < section.Columns.Count; c++)
            table.AddColumn(Unit.FromCentimeter(c == 0 ? unit * FirstColumnWeight : unit));

        var header = table.AddRow();
        header.HeadingFormat = true; // répété sur chaque page
        header.Shading.Color = Brand;
        header.Format.Font.Color = Colors.White;
        header.Format.Font.Bold = true;
        for (var c = 0; c < section.Columns.Count; c++) header.Cells[c].AddParagraph(section.Columns[c]);

        foreach (var cells in section.Rows)
        {
            var row = table.AddRow();
            for (var c = 0; c < cells.Length; c++) row.Cells[c].AddParagraph(Format(cells[c]));
        }

        if (section.Note is null) return;
        var note = page.AddParagraph(section.Note);
        note.Format.Font.Color = Muted;
        note.Format.SpaceBefore = 3;
    }

    public static string Format(ReportCell cell)
    {
        if (cell.Value is null) return "—";
        if (cell.Value is string text) return text;
        var number = Convert.ToDouble(cell.Value);
        return cell.Format switch
        {
            CellFormat.Percent => number.ToString("P0", French),
            CellFormat.Change => (number > 0 ? "+" : "") + number.ToString("P0", French),
            CellFormat.Money => $"{number.ToString("N3", French)} TND",
            CellFormat.Duration => Duration(number),
            _ => number.ToString("#,##0.#", French),
        };
    }

    private static string Duration(double seconds)
    {
        var s = (long)Math.Round(seconds);
        return s < 60 ? $"{s} s"
            : s < 3600 ? $"{s / 60} min {s % 60:00}"
            : $"{s / 3600} h {s % 3600 / 60:00}";
    }
}

/// <summary>
/// PDFsharp ne lit pas les polices du système : DejaVu dans le conteneur Linux (paquet
/// fonts-dejavu-core), Arial sur un poste Windows.
/// </summary>
public class ReportFontResolver : IFontResolver
{
    public const string Family = "Report";

    private static readonly (string Regular, string Bold)[] Candidates =
    [
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
        (Windows("arial.ttf"), Windows("arialbd.ttf")),
    ];

    private static string Windows(string file) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);

    // L'italique n'est pas utilisé : seules deux graisses sont nécessaires.
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "bold" : "regular");

    public byte[] GetFont(string faceName)
    {
        var path = Candidates.Select(c => faceName == "bold" ? c.Bold : c.Regular).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Aucune police disponible pour les rapports PDF (installer fonts-dejavu-core).");
        return File.ReadAllBytes(path);
    }
}
