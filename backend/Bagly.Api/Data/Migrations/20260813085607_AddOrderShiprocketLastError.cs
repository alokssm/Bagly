using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderShiprocketLastError : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShiprocketLastError",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShiprocketLastError",
                table: "Orders");
        }
    }
}
