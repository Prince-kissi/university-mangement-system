using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComplaintSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    IndexNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Department = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Programme = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Level = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    TelephoneNumber = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Students_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "Students"
                    ("Id", "UserId", "FirstName", "LastName", "Email", "ReferenceNumber",
                     "Department", "Level", "TelephoneNumber", "DateOfBirth",
                     "CreatedAt", "UpdatedAt", "ConcurrencyToken")
                SELECT
                    u."Id", u."Id", u."FirstName", u."LastName", LOWER(TRIM(u."Email")),
                    u."StudentReferenceNumber", u."StudentDepartment", u."StudentLevel",
                    u."PhoneNumber", u."DateOfBirth", u."CreatedAt", u."UpdatedAt", u."ConcurrencyToken"
                FROM "AspNetUsers" AS u
                WHERE u."StudentReferenceNumber" IS NOT NULL
                  AND u."Email" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Students_Email",
                table: "Students",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_IndexNumber",
                table: "Students",
                column: "IndexNumber",
                unique: true,
                filter: "\"IndexNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Students_ReferenceNumber",
                table: "Students",
                column: "ReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_UserId",
                table: "Students",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Students");
        }
    }
}
