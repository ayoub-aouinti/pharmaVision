using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using PharmaVision.Domain;

namespace PharmaVision.Infrastructure;

/// <summary>Ligne de l'agrégat continu TimescaleDB « events_hourly ».</summary>
public class HourlyAggregate
{
    public DateTime Bucket { get; set; }
    public int ZoneId { get; set; }
    public EventType Type { get; set; }
    public long Count { get; set; }
    public double Total { get; set; }
}

public class PharmaDbContext(DbContextOptions<PharmaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Pharmacy> Pharmacies => Set<Pharmacy>();
    public DbSet<EdgeDevice> Devices => Set<EdgeDevice>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<ZoneEvent> Events => Set<ZoneEvent>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<HourlyAggregate> Hourly => Set<HourlyAggregate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<EdgeDevice>().HasIndex(d => d.ApiKey).IsUnique();

        b.Entity<Zone>().Property(z => z.Polygon).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Web),
            v => JsonSerializer.Deserialize<List<ZonePoint>>(v, JsonSerializerOptions.Web) ?? new List<ZonePoint>(),
            new ValueComparer<List<ZonePoint>>(
                (a, c) => a!.SequenceEqual(c!),
                v => v.Aggregate(0, (h, p) => HashCode.Combine(h, p)),
                v => v.ToList()));

        b.Entity<ZoneEvent>(e =>
        {
            e.ToTable("zone_events");
            // La clé d'une hypertable doit contenir la colonne de temps.
            e.HasKey(x => new { x.Id, x.Ts });
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Ts).HasColumnName("ts");
            e.Property(x => x.ZoneId).HasColumnName("zone_id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Value).HasColumnName("value");
            e.Property(x => x.TrackId).HasColumnName("track_id");
            e.HasIndex(x => new { x.ZoneId, x.Type, x.Ts });
            e.HasOne<Zone>().WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<HourlyAggregate>(e =>
        {
            e.HasNoKey().ToView("events_hourly");
            e.Property(x => x.Bucket).HasColumnName("bucket");
            e.Property(x => x.ZoneId).HasColumnName("zone_id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Count).HasColumnName("count");
            e.Property(x => x.Total).HasColumnName("total");
        });

        b.Entity<Sale>().HasIndex(s => new { s.PharmacyId, s.Ts });
        b.Entity<Sale>().Property(s => s.Amount).HasPrecision(12, 3);

        b.Entity<Alert>().HasIndex(a => new { a.PharmacyId, a.Status });
        b.Entity<Alert>().HasOne<Pharmacy>().WithMany().HasForeignKey(a => a.PharmacyId);
        b.Entity<Alert>().HasOne<Zone>().WithMany().HasForeignKey(a => a.ZoneId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Alert>().HasOne<Camera>().WithMany().HasForeignKey(a => a.CameraId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Alert>().HasOne<AlertRule>().WithMany().HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Utilisé uniquement par « dotnet ef ».</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PharmaDbContext>
{
    public PharmaDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PharmaDbContext>()
        .UseNpgsql("Host=localhost;Port=5450;Database=pharmavision;Username=pharma;Password=pharma_dev")
        .Options);
}
