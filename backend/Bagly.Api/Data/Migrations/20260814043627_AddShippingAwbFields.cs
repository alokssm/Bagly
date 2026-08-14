using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingAwbFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualShippingCharge",
                table: "OrderShiprocketShipments",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AwbAssignedAt",
                table: "OrderShiprocketShipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwbCode",
                table: "OrderShiprocketShipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CourierId",
                table: "OrderShiprocketShipments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourierName",
                table: "OrderShiprocketShipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyToShipAt",
                table: "OrderShiprocketShipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingStatus",
                table: "OrderShiprocketShipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderShiprocketShipments_AwbCode",
                table: "OrderShiprocketShipments",
                column: "AwbCode");

            migrationBuilder.CreateIndex(
                name: "IX_OrderShiprocketShipments_ShippingStatus",
                table: "OrderShiprocketShipments",
                column: "ShippingStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderShiprocketShipments_AwbCode",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropIndex(
                name: "IX_OrderShiprocketShipments_ShippingStatus",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "ActualShippingCharge",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "AwbAssignedAt",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "AwbCode",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "CourierId",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "CourierName",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "ReadyToShipAt",
                table: "OrderShiprocketShipments");

            migrationBuilder.DropColumn(
                name: "ShippingStatus",
                table: "OrderShiprocketShipments");
        }
    }
}
