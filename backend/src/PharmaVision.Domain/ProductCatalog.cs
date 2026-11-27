namespace PharmaVision.Domain;

/// <summary>
/// Catalogue fictif : sert au seed et au boîtier simulé. En production, il viendra du logiciel
/// de gestion d'officine avec les ventes.
/// </summary>
public static class ProductCatalog
{
    public static readonly IReadOnlyList<Product> All =
    [
        .. Family("Dermo-cosmétique", "DER",
            ("Avène", "Crème hydratante 40 ml", 42m), ("Avène", "Eau thermale 300 ml", 28m),
            ("La Roche-Posay", "Gel nettoyant 400 ml", 46m), ("La Roche-Posay", "Baume réparateur 40 ml", 34m),
            ("Bioderma", "Eau micellaire 500 ml", 39m)),
        .. Family("Bébé", "BEB",
            ("Mustela", "Gel lavant 500 ml", 31m), ("Mustela", "Crème change 100 ml", 22m),
            ("Chicco", "Biberon 250 ml", 24m), ("Uriage", "Eau nettoyante 500 ml", 29m)),
        .. Family("Solaires", "SOL",
            ("SVR", "Écran SPF 50 200 ml", 52m), ("Avène", "Fluide SPF 50 50 ml", 44m),
            ("Vichy", "Lait solaire SPF 30 300 ml", 47m)),
        .. Family("Orthopédie", "ORT",
            ("Orliman", "Genouillère", 58m), ("Orliman", "Attelle de poignet", 49m),
            ("Thuasne", "Ceinture lombaire", 85m), ("Thuasne", "Bas de contention", 42m)),
        .. Family("Médicaments conseil", "MED",
            ("Doliprane", "1 g, boîte de 8", 6m), ("Humex", "Rhume, boîte de 16", 14m),
            ("Gaviscon", "Suspension 250 ml", 16m)),
        .. Family("Hygiène", "HYG", ("Elgydium", "Dentifrice 75 ml", 12m), ("Saforelle", "Soin lavant 250 ml", 24m)),
        .. Family("Compléments", "CPL",
            ("Arkopharma", "Magnésium, 30 gélules", 26m), ("Forté Pharma", "Vitamines, 28 comprimés", 33m)),
        .. Family("Ordonnance", "ORD", ("", "Médicaments sur ordonnance", 22m)),
    ];

    private static readonly ILookup<string, Product> ByFamily = All.ToLookup(p => p.Family);

    /// <summary>Produits d'une famille ; liste vide pour une famille non référencée.</summary>
    public static IReadOnlyList<Product> Of(string family) => ByFamily[family].ToList();

    private static IEnumerable<Product> Family(string family, string prefix, params (string Brand, string Name, decimal Price)[] items) =>
        items.Select((p, i) => new Product
        {
            Code = $"{prefix}-{i + 1:00}",
            Name = p.Name,
            Brand = p.Brand.Length == 0 ? null : p.Brand,
            Family = family,
            Price = p.Price,
        });
}
