using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PharmaVision.Domain;

namespace PharmaVision.Infrastructure;

/// <summary>Écriture en bloc des ventes (COPY), bien plus rapide que des insertions une à une.</summary>
public static class SalesCopy
{
    /// <summary>Participe à la transaction en cours sur la connexion du contexte.</summary>
    public static async Task CopySalesAsync(this PharmaDbContext db, IEnumerable<Sale> sales, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var writer = await connection.BeginBinaryImportAsync(
            """
            COPY "Sales" ("PharmacyId", "Ts", "TicketNo", "ProductCode", "Family", "Quantity", "Amount", "ImportId")
            FROM STDIN (FORMAT BINARY)
            """, ct);
        foreach (var s in sales)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(s.PharmacyId, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(s.Ts, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(s.TicketNo, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(s.ProductCode, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(s.Family, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(s.Quantity, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(s.Amount, NpgsqlDbType.Numeric, ct);
            if (s.ImportId is { } id) await writer.WriteAsync(id, NpgsqlDbType.Integer, ct);
            else await writer.WriteNullAsync(ct);
        }
        await writer.CompleteAsync(ct);
    }
}
