namespace PharmaVision.Api.Services.Reports;

public enum CellFormat { Text, Integer, Percent, Change, Money, Duration }

/// <summary>Valeur brute et façon de l'afficher ; chaque rendu (PDF, Excel) choisit sa présentation.</summary>
public record ReportCell(object? Value, CellFormat Format = CellFormat.Text)
{
    public static ReportCell Text(string? value) => new(value);
    public static ReportCell Integer(double? value) => new(value, CellFormat.Integer);
    /// <summary>Fraction : 0,42 pour 42 %.</summary>
    public static ReportCell Percent(double? value) => new(value, CellFormat.Percent);
    /// <summary>Évolution, affichée avec son signe ; arrondie au point pour éviter « -0 % ».</summary>
    public static ReportCell Change(double? value) =>
        new(value is { } v ? Math.Round(v, 2) + 0.0 : null, CellFormat.Change);
    public static ReportCell Money(decimal value) => new(value, CellFormat.Money);
    public static ReportCell Duration(double? seconds) => new(seconds, CellFormat.Duration);
}

/// <param name="Sheet">Nom court de l'onglet Excel (31 caractères au maximum).</param>
public record ReportSection(
    string Title, string Sheet, IReadOnlyList<string> Columns, IReadOnlyList<ReportCell[]> Rows, string? Note = null);

public record Report(string Title, string Subtitle, IReadOnlyList<string> Notes, IReadOnlyList<ReportSection> Sections);
