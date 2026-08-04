using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteHits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteHits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteHits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteHits_Country",
                table: "SiteHits",
                column: "Country");

            migrationBuilder.CreateIndex(
                name: "IX_SiteHits_OccurredAtUtc",
                table: "SiteHits",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SiteHits_SessionId",
                table: "SiteHits",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteHits");
        }
    }
}
