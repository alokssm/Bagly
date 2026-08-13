using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiPickupShiprocket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShiprocketPickupLocation",
                table: "Products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderShiprocketShipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupLocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShiprocketOrderId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShiprocketShipmentId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderShiprocketShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderShiprocketShipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ShiprocketPickupLocation",
                table: "Products",
                column: "ShiprocketPickupLocation");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShiprocketShipments_OrderId",
                table: "OrderShiprocketShipments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShiprocketShipments_OrderId_PickupLocation",
                table: "OrderShiprocketShipments",
                columns: new[] { "OrderId", "PickupLocation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderShiprocketShipments_ShiprocketOrderId",
                table: "OrderShiprocketShipments",
                column: "ShiprocketOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderShiprocketShipments");

            migrationBuilder.DropIndex(
                name: "IX_Products_ShiprocketPickupLocation",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShiprocketPickupLocation",
                table: "Products");
        }
    }
}
