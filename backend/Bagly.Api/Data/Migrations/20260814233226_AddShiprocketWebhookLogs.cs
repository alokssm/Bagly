using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShiprocketWebhookLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiprocketWebhookLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    HeadersJson = table.Column<string>(type: "text", nullable: true),
                    RequestBody = table.Column<string>(type: "text", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    ProcessedOk = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MatchedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchedShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MappedStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiprocketWebhookLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiprocketWebhookLogs_MatchedOrderId",
                table: "ShiprocketWebhookLogs",
                column: "MatchedOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiprocketWebhookLogs_MatchedShipmentId",
                table: "ShiprocketWebhookLogs",
                column: "MatchedShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiprocketWebhookLogs_ProcessedOk",
                table: "ShiprocketWebhookLogs",
                column: "ProcessedOk");

            migrationBuilder.CreateIndex(
                name: "IX_ShiprocketWebhookLogs_ReceivedAtUtc",
                table: "ShiprocketWebhookLogs",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiprocketWebhookLogs");
        }
    }
}
