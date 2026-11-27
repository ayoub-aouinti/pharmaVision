using ClosedXML.Excel;

namespace PharmaVision.Api.Services.Reports;

/// <summary>Un onglet de présentation puis un par section ; les nombres restent exploitables dans Excel.</summary>
public static class ExcelRenderer
{
    public static byte[] Render(Report report)
    {
        using var workbook = new XLWorkbook();

        var cover = workbook.AddWorksheet("Rapport");
        cover.Cell(1, 1).Value = report.Title;
        cover.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16);
        cover.Cell(2, 1).Value = report.Subtitle;
        for (var i = 0; i < report.Notes.Count; i++) cover.Cell(4 + i, 1).Value = report.Notes[i];
        cover.Column(1).Width = 110;
        cover.Column(1).Style.Alignment.WrapText = true;

        foreach (var section in report.Sections)
        {
            var sheet = workbook.AddWorksheet(section.Sheet);
            for (var c = 0; c < section.Columns.Count; c++)
            {
                var header = sheet.Cell(1, c + 1);
                header.Value = section.Columns[c];
                header.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#0B6E4C"));
            }
            for (var r = 0; r < section.Rows.Count; r++)
                for (var c = 0; c < section.Rows[r].Length; c++)
                    Write(sheet.Cell(r + 2, c + 1), section.Rows[r][c]);

            sheet.SheetView.FreezeRows(1);
            if (section.Note is not null) sheet.Cell(section.Rows.Count + 3, 1).Value = section.Note;
            // Largeur calculée sur le texte : l'ajustement automatique dépend des polices du serveur.
            for (var c = 0; c < section.Columns.Count; c++)
            {
                var longest = section.Rows.Select(row => (row[c].Value as string)?.Length ?? 0)
                    .Append(section.Columns[c].Length).Max();
                sheet.Column(c + 1).Width = Math.Clamp(longest + 4, 14, 60);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Un texte est enregistré comme valeur : il n'est jamais interprété comme une formule.</summary>
    private static void Write(IXLCell cell, ReportCell source)
    {
        if (source.Value is null) return;
        if (source.Value is string text)
        {
            cell.Value = text;
            return;
        }

        var number = Convert.ToDouble(source.Value);
        // Excel exprime les durées en fraction de jour.
        cell.Value = source.Format == CellFormat.Duration ? number / 86_400 : number;
        cell.Style.NumberFormat.Format = source.Format switch
        {
            CellFormat.Percent => "0 %",
            CellFormat.Change => "+0 %;-0 %;0 %",
            CellFormat.Money => "#,##0.000 \"TND\"",
            CellFormat.Duration => "[h]:mm:ss",
            _ => "#,##0.#",
        };
    }
}
