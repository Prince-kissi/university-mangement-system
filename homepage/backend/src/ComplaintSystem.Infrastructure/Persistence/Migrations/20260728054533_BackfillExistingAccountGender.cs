using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComplaintSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillExistingAccountGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers"
                SET "Gender" = 'Male'
                WHERE "Gender" IS NULL OR BTRIM("Gender") = '';

                UPDATE "Students"
                SET "Gender" = 'Male'
                WHERE "Gender" IS NULL OR BTRIM("Gender") = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous null/blank values cannot be distinguished from genders
            // explicitly recorded as Male, so this data backfill is irreversible.
        }
    }
}
