using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComplaintSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionalStudentIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityProvider",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageUrl",
                table: "AspNetUsers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSubject",
                table: "AspNetUsers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IdentityProvider_ProviderSubject",
                table: "AspNetUsers",
                columns: new[] { "IdentityProvider", "ProviderSubject" },
                unique: true,
                filter: "\"IdentityProvider\" IS NOT NULL AND \"ProviderSubject\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_IdentityProvider_ProviderSubject",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IdentityProvider",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfileImageUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProviderSubject",
                table: "AspNetUsers");
        }
    }
}
