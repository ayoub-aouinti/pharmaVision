using System.Globalization;

namespace PharmaVision.Domain.Import;

public record ImportError(int Line, string Message);

/// <param name="Ts">Heure locale de l'officine, telle qu'écrite dans le fichier.</param>
public record SaleLine(int Line, DateTime Ts, string Ticket, string Code, string? Family, int Quantity, decimal Amount);

public record ParsedFile<T>(List<T> Lines, List<ImportError> Errors);

/// <summary>Format d'import des ventes : une ligne par produit d'un ticket.</summary>
public static class SalesFile
{
    public const int MaxRows = 100_000;

    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["date"] = ["date", "datevente", "datetime", "horodatage"],
        ["time"] = ["heure", "time"],
        ["ticket"] = ["ticket", "numeroticket", "nticket", "ticketno", "facture", "numero"],
        ["code"] = ["code", "codeproduit", "codearticle", "reference", "codebarre", "ean"],
        ["family"] = ["famille", "family", "categorie", "rayon"],
        ["quantity"] = ["quantite", "qte", "quantity", "qty"],
        ["amount"] = ["montant", "montantttc", "total", "totalttc", "amount"],
    };

    private static readonly string[] DateFormats =
    [
        "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss", "yyyy-MM-dd H:mm", "yyyy-MM-dd H:mm:ss",
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "d-M-yyyy H:mm", "d-M-yyyy H:mm:ss",
    ];

    public static ParsedFile<SaleLine> Parse(byte[] content)
    {
        var rows = Csv.Parse(Csv.Decode(content));
        var errors = new List<ImportError>();
        var lines = new List<SaleLine>();
        if (rows.Count < 2) return new(lines, [new(1, "Le fichier ne contient aucune vente")]);
        if (rows.Count - 1 > MaxRows) return new(lines, [new(1, $"Plus de {MaxRows} lignes : découpez le fichier")]);

        var columns = Csv.Columns(rows[0].Fields, Aliases);
        var missing = new[] { ("date", "date"), ("ticket", "ticket"), ("code", "code"), ("quantity", "quantite"), ("amount", "montant") }
            .Where(c => !columns.ContainsKey(c.Item1)).Select(c => c.Item2).ToList();
        if (missing.Count > 0) return new(lines, [new(rows[0].Line, $"Colonnes manquantes : {string.Join(", ", missing)}")]);

        foreach (var (line, fields) in rows.Skip(1))
        {
            string Value(string name) =>
                columns.TryGetValue(name, out var i) && i < fields.Length ? fields[i] : "";
            void Fail(string message) => errors.Add(new(line, message));

            var date = $"{Value("date")} {Value("time")}".Trim();
            if (!DateTime.TryParseExact(date, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            {
                Fail($"Date et heure illisibles : « {date} » (attendu jj/mm/aaaa hh:mm)");
                continue;
            }
            var ticket = Value("ticket");
            var code = Value("code");
            if (ticket.Length is 0 or > 40) { Fail("Numéro de ticket absent ou trop long"); continue; }
            if (code.Length is 0 or > 40) { Fail("Code produit absent ou trop long"); continue; }
            var family = Value("family");
            if (family.Length > 60) { Fail("Famille trop longue"); continue; }
            if (!Csv.TryNumber(Value("quantity"), out var quantity) || quantity != decimal.Truncate(quantity)
                || quantity == 0 || Math.Abs(quantity) > 10_000)
            {
                Fail($"Quantité invalide : « {Value("quantity")} » (nombre entier non nul)");
                continue;
            }
            if (!Csv.TryNumber(Value("amount"), out var amount) || Math.Abs(amount) > 1_000_000)
            {
                Fail($"Montant invalide : « {Value("amount")} »");
                continue;
            }
            lines.Add(new SaleLine(line, ts, ticket, code, family.Length == 0 ? null : family,
                (int)quantity, Math.Round(amount, 3)));
        }
        return new(lines, errors);
    }
}

/// <summary>Format d'import du catalogue : code, nom, marque (facultative), famille, prix (facultatif).</summary>
public static class CatalogFile
{
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["code"] = ["code", "codeproduit", "codearticle", "reference", "codebarre", "ean"],
        ["name"] = ["nom", "designation", "libelle", "produit", "name"],
        ["brand"] = ["marque", "brand", "laboratoire"],
        ["family"] = ["famille", "family", "categorie", "rayon"],
        ["price"] = ["prix", "prixttc", "price"],
    };

    public static ParsedFile<Product> Parse(byte[] content)
    {
        var rows = Csv.Parse(Csv.Decode(content));
        var errors = new List<ImportError>();
        var products = new Dictionary<string, Product>();
        if (rows.Count < 2) return new([], [new(1, "Le fichier ne contient aucun produit")]);
        if (rows.Count - 1 > SalesFile.MaxRows) return new([], [new(1, $"Plus de {SalesFile.MaxRows} lignes")]);

        var columns = Csv.Columns(rows[0].Fields, Aliases);
        var missing = new[] { ("code", "code"), ("name", "nom"), ("family", "famille") }
            .Where(c => !columns.ContainsKey(c.Item1)).Select(c => c.Item2).ToList();
        if (missing.Count > 0) return new([], [new(rows[0].Line, $"Colonnes manquantes : {string.Join(", ", missing)}")]);

        foreach (var (line, fields) in rows.Skip(1))
        {
            string Value(string name) => columns.TryGetValue(name, out var i) && i < fields.Length ? fields[i] : "";
            var (code, name, brand, family) = (Value("code"), Value("name"), Value("brand"), Value("family"));
            if (code.Length is 0 or > 40 || name.Length is 0 or > 120 || family.Length is 0 or > 60 || brand.Length > 60)
            {
                errors.Add(new(line, "Code, nom et famille sont obligatoires, avec une longueur raisonnable"));
                continue;
            }
            var price = 0m;
            if (Value("price").Length > 0 && (!Csv.TryNumber(Value("price"), out price) || price < 0 || price > 1_000_000))
            {
                errors.Add(new(line, $"Prix invalide : « {Value("price")} »"));
                continue;
            }
            if (!products.TryAdd(code, new Product
                {
                    Code = code, Name = name, Brand = brand.Length == 0 ? null : brand, Family = family, Price = price,
                }))
                errors.Add(new(line, $"Code en double dans le fichier : « {code} »"));
        }
        return new([.. products.Values], errors);
    }
}
