using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComplaintSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentProfileContactDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdmissionYear",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacultyOrSchool",
                table: "Students",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalEmail",
                table: "Students",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResidentialAddress",
                table: "Students",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdmissionYear",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "FacultyOrSchool",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PersonalEmail",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ResidentialAddress",
                table: "Students");
        }
    }
}
