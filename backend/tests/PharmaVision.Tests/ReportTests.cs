using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using PharmaVision.Api.Services.Reports;
using PharmaVision.Domain;
using PharmaVision.Infrastructure;

namespace PharmaVision.Tests;

public class ReportTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<HttpClient> SignedInWithSales()
    {
        var client = await _api.SignedInAs("equipe@test");
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        db.Zones.Single(z => z.Id == 1).Brands = "Mustela";
        var ts = DateTime.UtcNow.AddMinutes(-5);
        foreach (var level in new[] { 80.0, 70, 60, 10 })
            db.Events.Add(new ZoneEvent { Ts = ts = ts.AddSeconds(1), ZoneId = 1, Type = EventType.Fill, Value = level });
        db.Products.AddRange(
            // Un nom de produit commençant par « = » ne doit pas devenir une formule.
            new Product { Code = "A", Name = "=1+1", Brand = "Mustela", Family = "Bébé", Price = 10 },
            new Product { Code = "B", Name = "Biberon", Brand = "Chicco", Family = "Bébé", Price = 30 });
        foreach (var (code, amount) in new[] { ("A", 20m), ("B", 60m) })
            db.Sales.Add(new Sale
            {
                PharmacyId = 1, Ts = ts, TicketNo = "T1", ProductCode = code, Family = "Bébé", Quantity = 1, Amount = amount,
            });
        await db.SaveChangesAsync();
        return client;
    }

    [Theory]
    [InlineData("synthese")]
    [InlineData("zones")]
    [InlineData("produits")]
    [InlineData("laboratoire")]
    public async Task Every_report_downloads_as_pdf_and_excel(string kind)
    {
        var client = await SignedInWithSales();
        var url = $"/api/reports/{kind}?pharmacyId=1&period=live&brand=Mustela";

        var pdf = await client.GetAsync($"{url}&format=pdf");
        var excel = await client.GetAsync($"{url}&format=xlsx");

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("%PDF"u8.ToArray(), (await pdf.Content.ReadAsByteArrayAsync())[..4]);
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.EndsWith(".xlsx", excel.Content.Headers.ContentDisposition!.FileName);
        using var workbook = new XLWorkbook(await excel.Content.ReadAsStreamAsync());
        Assert.True(workbook.Worksheets.Count >= 3);
    }

    [Fact]
    public async Task Laboratory_report_covers_one_brand_only()
    {
        var client = await SignedInWithSales();

        var response = await client.GetAsync("/api/reports/laboratoire?pharmacyId=1&period=live&brand=Mustela&format=xlsx");

        Assert.Contains("laboratoire-mustela-ville-1", response.Content.Headers.ContentDisposition!.FileName);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var sales = workbook.Worksheet("Ventes");
        Assert.Equal(20, sales.Cell(2, 2).GetDouble());
        Assert.Equal(0.25, sales.Cell(4, 2).GetDouble(), 3);

        var products = workbook.Worksheet("Produits");
        Assert.Equal("=1+1", products.Cell(2, 1).GetString());
        Assert.False(products.Cell(2, 1).HasFormula);
        Assert.True(products.Cell(3, 1).IsEmpty());

        // Trois relevés sur quatre au-dessus du seuil de 30 %.
        var visibility = workbook.Worksheet("Visibilité");
        Assert.Equal("Rayon", visibility.Cell(2, 1).GetString());
        Assert.Equal(0.75, visibility.Cell(2, 6).GetDouble(), 3);
    }

    [Fact]
    public async Task Brand_list_comes_from_the_catalogue()
    {
        var client = await SignedInWithSales();

        Assert.Equal(["Chicco", "Mustela"], await client.GetFromJsonAsync<List<string>>("/api/reports/brands"));
    }

    [Theory]
    [InlineData("/api/reports/synthese?pharmacyId=2", HttpStatusCode.Forbidden)]
    [InlineData("/api/reports/inconnu?pharmacyId=1", HttpStatusCode.NotFound)]
    [InlineData("/api/reports/7?pharmacyId=1", HttpStatusCode.NotFound)]
    [InlineData("/api/reports/synthese?pharmacyId=1&format=exe", HttpStatusCode.BadRequest)]
    [InlineData("/api/reports/laboratoire?pharmacyId=1", HttpStatusCode.BadRequest)]
    [InlineData("/api/reports/laboratoire?pharmacyId=1&brand=Inconnue", HttpStatusCode.NotFound)]
    public async Task Invalid_requests_are_refused(string url, HttpStatusCode expected)
    {
        var client = await SignedInWithSales();

        Assert.Equal(expected, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _api.Anonymous().GetAsync("/api/reports/synthese?pharmacyId=1")).StatusCode);

    [Fact]
    public void Tiny_variations_are_shown_as_zero() =>
        Assert.Equal("0 %", PdfRenderer.Format(ReportCell.Change(-0.003)).Replace('\u00a0', ' ').Replace('\u202f', ' '));

    [Theory]
    [InlineData(0.426, CellFormat.Percent, "43 %")]
    [InlineData(0.02, CellFormat.Change, "+2 %")]
    [InlineData(75, CellFormat.Duration, "1 min 15")]
    [InlineData(7260, CellFormat.Duration, "2 h 01")]
    public void Pdf_values_are_written_in_french(double value, CellFormat format, string expected) =>
        // La culture française sépare avec des espaces insécables.
        Assert.Equal(expected, PdfRenderer.Format(new ReportCell(value, format)).Replace(' ', ' ').Replace(' ', ' '));
}
