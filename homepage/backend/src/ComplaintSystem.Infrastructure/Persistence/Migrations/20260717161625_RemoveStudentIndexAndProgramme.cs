using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComplaintSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStudentIndexAndProgramme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_StudentIndexNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProgrammeCode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StudentIndexNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StudentProgramme",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProgrammeCode",
                table: "AspNetUsers",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudentIndexNumber",
                table: "AspNetUsers",
                type: "character varying(17)",
                maxLength: 17,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudentProgramme",
                table: "AspNetUsers",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_StudentIndexNumber",
                table: "AspNetUsers",
                column: "StudentIndexNumber",
                unique: true,
                filter: "\"StudentIndexNumber\" IS NOT NULL");
        }
    }
}
