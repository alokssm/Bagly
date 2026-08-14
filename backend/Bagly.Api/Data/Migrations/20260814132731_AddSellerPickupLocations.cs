using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerPickupLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerPickupLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupLocation = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    Address = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Address2 = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    City = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    State = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PinCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Lat = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Long = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Gstin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShiprocketSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ShiprocketPickupId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerPickupLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerPickupLocations_SellerUsers_SellerUserId",
                        column: x => x.SellerUserId,
                        principalTable: "SellerUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerPickupLocations_SellerUserId",
                table: "SellerPickupLocations",
                column: "SellerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPickupLocations_SellerUserId_PickupLocation",
                table: "SellerPickupLocations",
                columns: new[] { "SellerUserId", "PickupLocation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerPickupLocations");
        }
    }
}
