using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "SellerUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "SellerUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "SellerUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SellerUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gstin",
                table: "SellerUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pincode",
                table: "SellerUsers",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileSubmittedAt",
                table: "SellerUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "SellerUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "SellerUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpiId",
                table: "SellerUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "City",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "Gstin",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "Pincode",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "ProfileSubmittedAt",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "State",
                table: "SellerUsers");

            migrationBuilder.DropColumn(
                name: "UpiId",
                table: "SellerUsers");
        }
    }
}
