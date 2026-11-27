using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PharmaVision.Api.Controllers;
using PharmaVision.Api.Services;
using PharmaVision.Domain;
using PharmaVision.Domain.Import;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

public class CsvTests
{
    private static ParsedFile<SaleLine> Parse(string text) => SalesFile.Parse(Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData("Date;Heure;N° Ticket;Code article;Famille;Qté;Montant TTC\r\n05/03/2026;9:07;12;A1;Bébé;2;1 234,500\r\n")]
    [InlineData("date,ticket,code,famille,quantite,montant\n2026-03-05 09:07:00,12,A1,Bébé,2,1234.5\n")]
    [InlineData("date\tticket\tcode\tfamille\tqty\ttotal\n5/3/2026 09:07\t12\tA1\tBébé\t2\t1234,5")]
    public void Common_export_layouts_are_read(string text)
    {
        var (lines, errors) = Parse(text);

        Assert.Empty(errors);
        Assert.Equal(new SaleLine(2, new DateTime(2026, 3, 5, 9, 7, 0), "12", "A1", "Bébé", 2, 1234.5m), Assert.Single(lines));
    }

    [Fact]
    public void Quoted_fields_may_contain_separators_quotes_and_line_breaks()
    {
        var rows = Csv.Parse("a;b\n\"x;y\";\"dit \"\"oui\"\"\nencore\"\n\nfin;1\n");

        Assert.Equal(["x;y", "dit \"oui\"\nencore"], rows[1].Fields);
        Assert.Equal((5, "fin"), (rows[2].Line, rows[2].Fields[0]));
    }

    [Fact]
    public void Windows_exports_keep_their_accents()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Assert.Equal("Bébé œuvre €", Csv.Decode(Encoding.GetEncoding(1252).GetBytes("Bébé œuvre €")));
        Assert.Equal("Bébé", Csv.Decode([0xEF, 0xBB, 0xBF, .. "Bébé"u8]));
    }

    [Theory]
    [InlineData("31/02/2026 10:00;1;A;1;5", "Date")]
    [InlineData("05/03/2026;1;A;1;5", "Date")] // heure absente
    [InlineData("05/03/2026 10:00;;A;1;5", "ticket")]
    [InlineData("05/03/2026 10:00;1;A;1,5;5", "Quantité")]
    [InlineData("05/03/2026 10:00;1;A;0;5", "Quantité")]
    [InlineData("05/03/2026 10:00;1;A;1;1.234,50", "Montant")]
    public void Invalid_lines_are_reported_with_their_number(string row, string expected)
    {
        var (lines, errors) = Parse($"date;ticket;code;quantite;montant\n{row}\n");

        Assert.Empty(lines);
        var error = Assert.Single(errors);
        Assert.Equal(2, error.Line);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Missing_columns_are_named() =>
        Assert.Contains("code, montant", Assert.Single(Parse("date;ticket;quantite\n1;2;3").Errors).Message);
}

public class ImportTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private static readonly DateTime Yesterday = DateTime.UtcNow.Date.AddDays(-1);
    private static readonly string Day = Yesterday.ToString("dd/MM/yyyy");

    private static readonly string Sales =
        $"date;ticket;code;famille;quantite;montant\n{Day} 10:00;1;A;;2;20,5\n{Day} 10:00;1;ZZ;Divers;1;4\n{Day} 11:00;2;A;;1;10\n";

    private static Task<HttpResponseMessage> Upload(HttpClient client, string url, string text) => client.PostAsync(url,
        new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(text)), "file", "ventes.csv" } });

    private static async Task<SalesImportReport> Import(HttpClient client, string options, string? text = null)
    {
        var response = await Upload(client, $"/api/sales/import?pharmacyId=1&{options}", text ?? Sales);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SalesImportReport>())!;
    }

    private T Database<T>(Func<PharmaDbContext, T> query)
    {
        using var scope = _api.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PharmaDbContext>());
    }

    private async Task<HttpClient> OwnerWithSimulatedSale()
    {
        var client = await _api.SignedInAs("titulaire@test");
        Database(db =>
        {
            db.Products.Add(new Product { Code = "A", Name = "Gel", Brand = "Mustela", Family = "Bébé" });
            db.Sales.Add(new Sale { PharmacyId = 1, Ts = Yesterday.AddHours(9), TicketNo = "sim", ProductCode = "X", Family = "Bébé", Quantity = 1, Amount = 1 });
            db.Sales.Add(new Sale { PharmacyId = 1, Ts = Yesterday.AddDays(-5), TicketNo = "ancien", ProductCode = "X", Family = "Bébé", Quantity = 1, Amount = 1 });
            return db.SaveChanges();
        });
        return client;
    }

    [Fact]
    public async Task Preview_writes_nothing_then_import_stores_the_sales()
    {
        var owner = await OwnerWithSimulatedSale();

        var preview = await Import(owner, "dryRun=true&replace=true");
        Assert.Equal((null, 3, 3, 2, 34.5m, 1, 1), (preview.ImportId, preview.Rows, preview.NewRows, preview.Tickets, preview.Revenue, preview.Replaced, preview.UnknownCodes));
        Assert.Equal(["Bébé", "Divers"], preview.FamiliesWithoutShelf);
        Assert.Equal(2, Database(db => db.Sales.Count()));

        var done = await Import(owner, "dryRun=false");
        Assert.NotNull(done.ImportId);
        var stored = Database(db => db.Sales.Where(s => s.ImportId != null).OrderBy(s => s.Ts).ToList());
        // La famille vient du catalogue quand le fichier n'en donne pas ; heure de Tunis = UTC+1.
        Assert.Equal(("Bébé", Yesterday.AddHours(9), $"{Yesterday:yyyyMMdd}-1"),
            (stored.First(s => s.ProductCode == "A").Family, stored[0].Ts, stored[0].TicketNo));
        Assert.Equal(5, Database(db => db.Sales.Count()));
    }

    [Fact]
    public async Task Importing_the_same_file_twice_adds_nothing()
    {
        var owner = await OwnerWithSimulatedSale();
        await Import(owner, "dryRun=false");

        var again = await Import(owner, "dryRun=false");

        Assert.Equal((null, 0, 3), (again.ImportId, again.NewRows, again.Duplicates));
        Assert.Equal(5, Database(db => db.Sales.Count()));
    }

    [Fact]
    public async Task Replacing_removes_only_other_sales_of_the_covered_days_and_import_can_be_cancelled()
    {
        var owner = await OwnerWithSimulatedSale();
        var done = await Import(owner, "dryRun=false&replace=true");

        Assert.Equal(1, done.Replaced);
        Assert.Equal(["ancien"], Database(db => db.Sales.Where(s => s.ImportId == null).Select(s => s.TicketNo).ToList()));
        var listed = await owner.GetFromJsonAsync<List<ImportDto>>("/api/sales/imports?pharmacyId=1");
        Assert.Equal(("ventes.csv", 3, "titulaire@test"), (listed![0].FileName, listed[0].Rows, listed[0].ImportedBy));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/sales/imports/{done.ImportId}")).StatusCode);
        Assert.Equal(1, Database(db => db.Sales.Count()));
    }

    [Fact]
    public async Task One_bad_line_refuses_the_whole_file()
    {
        var owner = await OwnerWithSimulatedSale();
        var future = DateTime.UtcNow.AddDays(3).ToString("dd/MM/yyyy");

        var report = await Import(owner, "dryRun=false&replace=true", Sales + $"{future} 10:00;3;A;;1;1\nn'importe quoi;4;A;;1;1\n");

        Assert.Equal((null, 2), (report.ImportId, report.ErrorCount));
        Assert.Equal([5, 6], report.Errors.Select(e => e.Line));
        Assert.Equal(2, Database(db => db.Sales.Count()));
    }

    [Fact]
    public async Task Import_is_reserved_to_the_owner_of_the_pharmacy()
    {
        var staff = await _api.SignedInAs("equipe@test");
        var owner = await _api.SignedInAs("titulaire@test");

        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(staff, "/api/sales/import?pharmacyId=1", Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(owner, "/api/sales/import?pharmacyId=2", Sales)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(_api.Anonymous(), "/api/sales/import?pharmacyId=1", Sales)).StatusCode);
        // Titulaire d'une seule des deux officines : le catalogue commun lui est fermé.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Upload(owner, "/api/catalog/import", "code;nom;famille\nA;Gel;Bébé")).StatusCode);
    }

    [Fact]
    public async Task Catalogue_import_adds_and_updates_products()
    {
        var owner = await _api.SignedInAs("titulaire@test");
        Database(db =>
        {
            db.Users.Single(u => u.Email == "titulaire@test").PharmacyIds = [1, 2];
            db.Products.Add(new Product { Code = "A", Name = "Ancien nom", Family = "Bébé" });
            return db.SaveChanges();
        });
        // Le changement d'officines ne vaut que pour une nouvelle session.
        owner = await _api.SignedInAs("titulaire@test");
        const string file = "code;designation;marque;famille;prix\nA;Gel;Mustela;Bébé;12,5\nB;Crème;;Bébé;\n";

        var response = await Upload(owner, "/api/catalog/import?dryRun=false", file);
        var report = await response.Content.ReadFromJsonAsync<CatalogImportReport>();

        Assert.Equal((true, 2, 1, 1), (report!.Applied, report.Rows, report.Added, report.Updated));
        Assert.Equal(("Gel", "Mustela", 12.5m), Database(db => db.Products.Where(p => p.Code == "A")
            .Select(p => ValueTuple.Create(p.Name, p.Brand, p.Price)).Single()));
    }
}
