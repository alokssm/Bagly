using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentStatusLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentStatusLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderShiprocketShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwbCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShiprocketShipmentId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RawJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentStatusLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentStatusLogs_OrderShiprocketShipments_OrderShiprocket~",
                        column: x => x.OrderShiprocketShipmentId,
                        principalTable: "OrderShiprocketShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShipmentStatusLogs_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentStatusLogs_CreatedAtUtc",
                table: "ShipmentStatusLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentStatusLogs_OrderId",
                table: "ShipmentStatusLogs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentStatusLogs_OrderShiprocketShipmentId",
                table: "ShipmentStatusLogs",
                column: "OrderShiprocketShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentStatusLogs_ToStatus",
                table: "ShipmentStatusLogs",
                column: "ToStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentStatusLogs");
        }
    }
}
