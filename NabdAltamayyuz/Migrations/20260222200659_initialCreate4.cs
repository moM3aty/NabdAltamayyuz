using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NabdAltamayyuz.Migrations
{
    /// <inheritdoc />
    public partial class initialCreate4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstLaborOfficeId",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstSequenceNumber",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstLaborOfficeId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EstSequenceNumber",
                table: "Companies");
        }
    }
}
