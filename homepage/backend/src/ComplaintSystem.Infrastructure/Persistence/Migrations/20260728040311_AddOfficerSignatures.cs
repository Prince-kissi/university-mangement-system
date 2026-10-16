using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComplaintSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficerSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponseInitials",
                table: "ComplaintResponses",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseSignatureFilePath",
                table: "ComplaintResponses",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SignatureIncluded",
                table: "ComplaintResponses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignedAt",
                table: "ComplaintResponses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByAdministrativeUnit",
                table: "ComplaintResponses",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SignedByOfficerId",
                table: "ComplaintResponses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByOfficerName",
                table: "ComplaintResponses",
                type: "character varying(170)",
                maxLength: 170,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Initials",
                table: "AspNetUsers",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureFilePath",
                table: "AspNetUsers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignatureUpdatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseInitials",
                table: "ComplaintResponses");

            migrationBuilder.DropColumn(
                name: "ResponseSignatureFilePath",
                table: "ComplaintResponses");

            migrationBuilder.DropColumn(
                name: "SignatureIncluded",
                table: "ComplaintResponses");

            migrationBuilder.DropColumn(
                name: "SignedAt",
                table: "ComplaintResponses");

            migrationBuilder.DropColumn(
                name: "SignedByAdministrativeUnit",
                table: "ComplaintResponses");

            migrationBuilder.DropColumn(
                name: "SignedByOfficerId",
                table: "ComplaintResponses");

            migrationBuilder.DropColumn(
                name: "SignedByOfficerName",
                table: "ComplaintResponses");

            migrationBuilder.DropColumn(
                name: "Initials",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignatureFilePath",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignatureUpdatedAt",
                table: "AspNetUsers");
        }
    }
}
