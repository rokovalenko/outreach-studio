using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OutreachStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    audience_size = table.Column<int>(type: "integer", nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    skip_reason = table.Column<string>(type: "text", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    claimed_by = table.Column<string>(type: "text", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_message_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    delivery_id = table.Column<long>(type: "bigint", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppressions",
                columns: table => new
                {
                    email = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppressions", x => x.email);
                });

            migrationBuilder.CreateTable(
                name: "user_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    tier = table.Column<string>(type: "text", nullable: false),
                    signup_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_active = table.Column<DateOnly>(type: "date", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    marketing_consent = table.Column<bool>(type: "boolean", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    points_balance = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approvals",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    reviewer = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_approvals_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign_versions",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rule_json = table.Column<string>(type: "jsonb", nullable: false),
                    channels = table.Column<string>(type: "text", nullable: false),
                    push_title = table.Column<string>(type: "text", nullable: false),
                    push_body = table.Column<string>(type: "text", nullable: false),
                    email_subject = table.Column<string>(type: "text", nullable: false),
                    email_body = table.Column<string>(type: "text", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    per_minute = table.Column<int>(type: "integer", nullable: false),
                    quiet_start_hour = table.Column<int>(type: "integer", nullable: false),
                    quiet_end_hour = table.Column<int>(type: "integer", nullable: false),
                    holdout_share = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_versions", x => new { x.campaign_id, x.number });
                    table.ForeignKey(
                        name: "fk_campaign_versions_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approvals_campaign_id",
                table: "approvals",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_campaign_id_status",
                table: "deliveries",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_campaign_id_user_id_channel",
                table: "deliveries",
                columns: new[] { "campaign_id", "user_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_provider_message_id",
                table: "deliveries",
                column: "provider_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_status_due_at",
                table: "deliveries",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_user_id_sent_at",
                table: "deliveries",
                columns: new[] { "user_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_started_at",
                table: "delivery_attempts",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_user_events_user_id_type_occurred_at",
                table: "user_events",
                columns: new[] { "user_id", "type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            // Every status change on a delivery is announced on the delivery_events channel, so the
            // live board in the web app listens instead of polling. The payload is what the board shows.
            migrationBuilder.Sql("""
                CREATE FUNCTION notify_delivery_change() RETURNS trigger AS $$
                BEGIN
                    PERFORM pg_notify('delivery_events', json_build_object(
                        'campaignId', NEW.campaign_id,
                        'deliveryId', NEW.id,
                        'userId', NEW.user_id,
                        'channel', NEW.channel,
                        'status', NEW.status,
                        'skipReason', NEW.skip_reason,
                        'provider', NEW.provider,
                        'attempts', NEW.attempts,
                        'error', NEW.last_error)::text);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER deliveries_notify
                AFTER UPDATE OF status ON deliveries
                FOR EACH ROW
                WHEN (OLD.status IS DISTINCT FROM NEW.status AND NEW.status <> 'Claimed')
                EXECUTE FUNCTION notify_delivery_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER deliveries_notify ON deliveries; DROP FUNCTION notify_delivery_change();");

            migrationBuilder.DropTable(
                name: "approvals");

            migrationBuilder.DropTable(
                name: "campaign_versions");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropTable(
                name: "delivery_attempts");

            migrationBuilder.DropTable(
                name: "suppressions");

            migrationBuilder.DropTable(
                name: "user_events");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "campaigns");
        }
    }
}
