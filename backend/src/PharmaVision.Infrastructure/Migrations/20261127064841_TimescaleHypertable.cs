using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaVision.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TimescaleHypertable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");
            migrationBuilder.Sql("SELECT create_hypertable('zone_events', 'ts', chunk_time_interval => INTERVAL '7 days');");

            // Un agrégat continu ne peut pas être créé dans une transaction.
            migrationBuilder.Sql(
                """
                CREATE MATERIALIZED VIEW events_hourly
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT time_bucket(INTERVAL '1 hour', ts) AS bucket, zone_id, type,
                       count(*) AS count, sum(value) AS total
                FROM zone_events
                GROUP BY 1, 2, 3
                WITH NO DATA;
                """, suppressTransaction: true);

            migrationBuilder.Sql(
                """
                SELECT add_continuous_aggregate_policy('events_hourly',
                    start_offset => INTERVAL '3 days',
                    end_offset => INTERVAL '1 hour',
                    schedule_interval => INTERVAL '10 minutes');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS events_hourly;", suppressTransaction: true);
        }
    }
}
