using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SellerUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SellerUsers_Email",
                table: "SellerUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerUsers_IsActive",
                table: "SellerUsers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SellerUsers_Status",
                table: "SellerUsers",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerUsers");
        }
    }
}
