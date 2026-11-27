using System.Globalization;
using System.Text;

namespace PharmaVision.Domain.Import;

/// <summary>Lecture des exports CSV des logiciels de caisse (« ; », « , » ou tabulation).</summary>
public static class Csv
{
    static Csv() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>UTF-8, ou Windows-1252 (exports Excel en français) si le fichier n'est pas de l'UTF-8 valide.</summary>
    public static string Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(content);
        }
    }

    /// <summary>Lignes non vides avec leur numéro dans le fichier ; le séparateur est déduit de l'en-tête.</summary>
    public static List<(int Line, string[] Fields)> Parse(string text)
    {
        var header = text.AsSpan(0, text.IndexOf('\n') is var end and >= 0 ? end : text.Length).ToString();
        var separator = new[] { ';', '\t', ',' }.MaxBy(c => header.Count(h => h == c));

        var rows = new List<(int, string[])>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        int line = 1, start = 1;

        void EndRow()
        {
            fields.Add(field.ToString().Trim());
            field.Clear();
            if (fields.Any(f => f.Length > 0)) rows.Add((start, fields.ToArray()));
            fields.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n') line++;
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"' && field.ToString().AsSpan().Trim().IsEmpty) { field.Clear(); quoted = true; }
            else if (c == separator) { fields.Add(field.ToString().Trim()); field.Clear(); }
            else if (c == '\n') { EndRow(); start = line; }
            else field.Append(c);
        }
        EndRow();
        return rows;
    }

    /// <summary>Position de chaque colonne attendue, quels que soient la casse, les accents et les espaces.</summary>
    public static Dictionary<string, int> Columns(string[] header, IReadOnlyDictionary<string, string[]> aliases)
    {
        var normalized = header.Select(Normalize).ToList();
        return aliases
            .Select(a => (a.Key, Index: normalized.FindIndex(h => a.Value.Contains(h))))
            .Where(a => a.Index >= 0)
            .ToDictionary(a => a.Key, a => a.Index);
    }

    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormD)
        .Where(c => char.IsLetterOrDigit(c))
        .Select(char.ToLowerInvariant).ToArray());

    /// <summary>Nombre avec virgule ou point décimal ; les espaces de milliers sont ignorés.</summary>
    public static bool TryNumber(string value, out decimal number)
    {
        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        number = 0;
        // « 1.234,5 » et « 1,234.5 » sont ambigus selon le logiciel : refusés plutôt que mal lus.
        if (compact.Count(c => c is ',' or '.') > 1) return false;
        return decimal.TryParse(compact.Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }
}
