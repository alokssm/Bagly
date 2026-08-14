using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentPickupAndTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PickupRequestedAt",
                table: "OrderShiprocketShipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupTokenNumber",
                table: "OrderShiprocketShipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingStatus",
                table: "OrderShiprocketShipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrackingStatusUpdatedAt",
                table: "OrderShiprocketShipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderShipmentTrackings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderShiprocketShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiprocketShipmentId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AwbCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RawJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderShipmentTrackings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderShipmentTrackings_OrderShiprocketShipments_OrderShipro~",
                        column: x => x.OrderShiprocketShipmentId,
                        principalTable: "OrderShiprocketShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderShipmentTrackings_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderShiprocketShipments_PickupRequestedAt",
                table: "OrderShiprocketShipments",
                column: "PickupRequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShiprocketShipments_TrackingStatus",
                table: "OrderShiprocketShipments",
                column: "TrackingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShipmentTrackings_ChangedAtUtc",
                table: "OrderShipmentTrackings",
                column: "ChangedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShipmentTrackings_OrderId",
                table: "OrderShipmentTrackings",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShipmentTrackings_OrderShiprocketShipmentId",
                table: "OrderShipmentTrackings",
                column: "OrderShiprocketShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShipmentTrackings_Status",
                table: "OrderShipmentTrackings",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderShipmentTrackings");

            migrationBuilder.DropIndex(
                name: "IX_OrderShiprocketShipments_PickupRequestedAt",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropIndex(
                name: "IX_OrderShiprocketShipments_TrackingStatus",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "PickupRequestedAt",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "PickupTokenNumber",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "TrackingStatus",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "TrackingStatusUpdatedAt",
                table: "OrderShiprocketShipments");
        }
    }
}
