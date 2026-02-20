using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BloodWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    adapter_key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_polled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_regions_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "current_reserves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_key = table.Column<string>(type: "text", nullable: false),
                    status_key = table.Column<string>(type: "text", nullable: false),
                    status_label = table.Column<string>(type: "text", nullable: false),
                    reference_date = table.Column<DateOnly>(type: "date", nullable: true),
                    captured_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_current_reserves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_current_reserves_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_current_reserves_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "donation_centers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    institution_code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    district_code = table.Column<string>(type: "text", nullable: true),
                    district_name = table.Column<string>(type: "text", nullable: true),
                    municipality_code = table.Column<string>(type: "text", nullable: true),
                    municipality_name = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    plus_code = table.Column<string>(type: "text", nullable: true),
                    schedule = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    mobile_phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donation_centers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_donation_centers_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_donation_centers_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_reserve_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_key = table.Column<string>(type: "text", nullable: false),
                    metric_key = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_events_current_reserves_current_reserve_id",
                        column: x => x.current_reserve_id,
                        principalTable: "current_reserves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_events_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_events_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    institution_code = table.Column<string>(type: "text", nullable: false),
                    institution_name = table.Column<string>(type: "text", nullable: false),
                    district_code = table.Column<string>(type: "text", nullable: true),
                    district_name = table.Column<string>(type: "text", nullable: true),
                    municipality_code = table.Column<string>(type: "text", nullable: true),
                    municipality_name = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    session_date = table.Column<DateOnly>(type: "date", nullable: true),
                    session_hours = table.Column<string>(type: "text", nullable: true),
                    access_code = table.Column<string>(type: "text", nullable: true),
                    state_code = table.Column<string>(type: "text", nullable: true),
                    session_type_code = table.Column<string>(type: "text", nullable: true),
                    session_type_name = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collection_sessions_donation_centers_donation_center_id",
                        column: x => x.donation_center_id,
                        principalTable: "donation_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_collection_sessions_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_sessions_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_key = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    scope_type = table.Column<string>(type: "text", nullable: false),
                    region_filter = table.Column<string>(type: "text", nullable: true),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metric_filter = table.Column<string>(type: "text", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    disabled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscriptions_donation_centers_institution_id",
                        column: x => x.institution_id,
                        principalTable: "donation_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscriptions_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliveries_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_deliveries_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "sources",
                columns: new[] { "Id", "adapter_key", "created_at_utc", "last_polled_at_utc", "name" },
                values: new object[] { new Guid("51abf65a-c68a-4bc7-b9f2-3f8f3a9bb2b1"), "pt-dador-ipst", new DateTime(2026, 2, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, "Portugal Dador/IPST" });

            migrationBuilder.CreateIndex(
                name: "IX_collection_sessions_donation_center_id",
                table: "collection_sessions",
                column: "donation_center_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_sessions_region_id",
                table: "collection_sessions",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_sessions_source_id_external_id",
                table: "collection_sessions",
                columns: new[] { "source_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_sessions_source_id_session_date",
                table: "collection_sessions",
                columns: new[] { "source_id", "session_date" });

            migrationBuilder.CreateIndex(
                name: "IX_current_reserves_region_id",
                table: "current_reserves",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_current_reserves_source_id_captured_at_utc",
                table: "current_reserves",
                columns: new[] { "source_id", "captured_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_current_reserves_source_id_region_id_metric_key",
                table: "current_reserves",
                columns: new[] { "source_id", "region_id", "metric_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_event_id_status",
                table: "deliveries",
                columns: new[] { "event_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_event_id_subscription_id",
                table: "deliveries",
                columns: new[] { "event_id", "subscription_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_subscription_id",
                table: "deliveries",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_donation_centers_region_id",
                table: "donation_centers",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_donation_centers_source_id_external_id",
                table: "donation_centers",
                columns: new[] { "source_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_donation_centers_source_id_institution_code",
                table: "donation_centers",
                columns: new[] { "source_id", "institution_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_donation_centers_source_id_region_id",
                table: "donation_centers",
                columns: new[] { "source_id", "region_id" });

            migrationBuilder.CreateIndex(
                name: "IX_events_current_reserve_id",
                table: "events",
                column: "current_reserve_id");

            migrationBuilder.CreateIndex(
                name: "IX_events_idempotency_key",
                table: "events",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_region_id",
                table: "events",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "IX_events_source_id_created_at_utc",
                table: "events",
                columns: new[] { "source_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_regions_source_id_key",
                table: "regions",
                columns: new[] { "source_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sources_adapter_key",
                table: "sources",
                column: "adapter_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_institution_id",
                table: "subscriptions",
                column: "institution_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_source_id_is_enabled",
                table: "subscriptions",
                columns: new[] { "source_id", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_source_id_scope_type_region_filter_institutio~",
                table: "subscriptions",
                columns: new[] { "source_id", "scope_type", "region_filter", "institution_id", "metric_filter", "is_enabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_sessions");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "current_reserves");

            migrationBuilder.DropTable(
                name: "donation_centers");

            migrationBuilder.DropTable(
                name: "regions");

            migrationBuilder.DropTable(
                name: "sources");
        }
    }
}
