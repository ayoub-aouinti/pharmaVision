using System.Globalization;
using System.Text;
using PharmaVision.Domain;

namespace PharmaVision.Api.Services;

/// <summary>Images fictives (SVG) en attendant les vrais flux : référence de caméra et vignettes floutées.</summary>
public static class SceneRenderer
{
    private const int Width = 1280;
    private const int Height = 720;
    private static readonly string[] Palette = ["#8FB8A8", "#C9D8D2", "#E3C9A8", "#A9C4DD", "#D8B4B4", "#B7C7A3", "#F0E6D2"];

    public static string Snapshot(Camera camera) => Wrap(Scene(camera), camera.Name);

    /// <summary>Vignette d'alerte : la scène, la zone concernée et des silhouettes floutées.</summary>
    public static string Thumbnail(Camera camera, Zone? zone, int seed, DateTime ts)
    {
        var body = new StringBuilder(Scene(camera));
        var center = (X: Width * 0.5, Y: Height * 0.6);
        if (zone is { Polygon.Count: > 1 })
        {
            var points = string.Join(' ', zone.Polygon.Select(p => $"{N(p.X * Width)},{N(p.Y * Height)}"));
            body.Append($"<polygon points='{points}' fill='#E8792B' fill-opacity='.12' stroke='#E8792B' stroke-width='5'/>");
            center = (zone.Polygon.Average(p => p.X) * Width, zone.Polygon.Max(p => p.Y) * Height);
        }
        var rng = new Random(seed);
        body.Append("<defs><filter id='blur' x='-50%' y='-50%' width='200%' height='200%'><feGaussianBlur stdDeviation='16'/></filter></defs><g filter='url(#blur)' fill='#33433D'>");
        for (var i = rng.Next(1, 3); i > 0; i--)
        {
            var x = center.X + rng.Next(-180, 180);
            var y = Math.Min(center.Y + rng.Next(-60, 60), Height - 60);
            body.Append($"<ellipse cx='{N(x)}' cy='{N(y)}' rx='58' ry='120'/><circle cx='{N(x)}' cy='{N(y - 150)}' r='44'/>");
        }
        body.Append("</g>");
        return Wrap(body.ToString(), $"{camera.Name} · {ts:dd/MM/yyyy HH:mm} UTC · personnes floutées");
    }

    private static string Wrap(string body, string caption) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='{Width}' height='{Height}' viewBox='0 0 {Width} {Height}'>{body}" +
        $"<rect x='16' y='16' width='{caption.Length * 12 + 32}' height='40' rx='8' fill='#0F1A17' fill-opacity='.72'/>" +
        $"<text x='32' y='43' font-family='Inter,Segoe UI,sans-serif' font-size='21' fill='#fff'>{System.Security.SecurityElement.Escape(caption)}</text></svg>";

    private static string Scene(Camera camera)
    {
        var s = new StringBuilder();
        var rng = new Random(camera.Id * 7919);
        s.Append("<rect width='1280' height='720' fill='#DCE5E2'/><path d='M0 520H1280V720H0Z' fill='#B9C6C1'/>");
        for (var x = -400; x <= 1680; x += 160)
            s.Append($"<path d='M640 330L{x} 720' stroke='#A9B8B2' stroke-width='2'/>");
        s.Append("<rect y='330' width='1280' height='190' fill='#DCE5E2'/>");

        switch (camera.Scene)
        {
            case "entrance":
                s.Append("<rect x='400' y='110' width='480' height='430' fill='#BFD9EA' stroke='#5C6F68' stroke-width='12'/><path d='M640 110V540' stroke='#5C6F68' stroke-width='8'/>");
                Shelf(s, rng, 60, 220, 240, 330);
                Shelf(s, rng, 980, 220, 240, 330);
                break;
            case "counter":
                Shelf(s, rng, 80, 90, 1120, 300);
                s.Append("<rect x='70' y='420' width='480' height='190' rx='10' fill='#0B6E4C'/><rect x='730' y='420' width='480' height='190' rx='10' fill='#0B6E4C'/>");
                s.Append("<rect x='240' y='360' width='130' height='90' rx='6' fill='#2B3A35'/><rect x='900' y='360' width='130' height='90' rx='6' fill='#2B3A35'/>");
                break;
            case "shelves":
                Shelf(s, rng, 55, 120, 550, 450);
                Shelf(s, rng, 675, 120, 550, 450);
                break;
            case "aisle":
                Shelf(s, rng, 30, 150, 385, 420);
                Shelf(s, rng, 455, 150, 370, 420);
                Shelf(s, rng, 865, 150, 385, 420);
                break;
            case "storage":
                Shelf(s, rng, 60, 140, 520, 400);
                s.Append("<rect x='780' y='200' width='390' height='340' rx='8' fill='#8A9893' stroke='#52625C' stroke-width='10'/><circle cx='1110' cy='370' r='24' fill='#52625C'/>");
                break;
            default:
                s.Append("<rect x='340' y='180' width='600' height='340' fill='#C4D1CC'/>");
                break;
        }
        return s.ToString();
    }

    private static void Shelf(StringBuilder s, Random rng, int x, int y, int w, int h)
    {
        s.Append($"<rect x='{x}' y='{y}' width='{w}' height='{h}' fill='#F7F9F8' stroke='#8FA099' stroke-width='5'/>");
        var rows = Math.Max(2, h / 105);
        var rowHeight = h / rows;
        for (var r = 0; r < rows; r++)
        {
            var bottom = y + (r + 1) * rowHeight - 8;
            for (var bx = x + 14; bx < x + w - 40;)
            {
                var bw = rng.Next(24, 48);
                var bh = rng.Next(rowHeight / 2, rowHeight - 22);
                if (rng.NextDouble() > 0.07)
                    s.Append($"<rect x='{bx}' y='{bottom - bh}' width='{bw}' height='{bh}' rx='3' fill='{Palette[rng.Next(Palette.Length)]}'/>");
                bx += bw + 7;
            }
            s.Append($"<rect x='{x}' y='{bottom}' width='{w}' height='8' fill='#8FA099'/>");
        }
    }

    private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
