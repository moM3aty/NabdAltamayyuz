using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NabdAltamayyuz.Migrations
{
    /// <inheritdoc />
    public partial class initialCreate3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "WorkTasks",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "WorkTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "WorkTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AllowedEmployees",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AllowedSubAccounts",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                table: "Companies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NationalAddressShortCode",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "NotificationDaysBeforeExpiry",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParentCompanyId",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTerm",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerEmployee",
                table: "Companies",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ResponsiblePerson",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionEndDate",
                table: "Companies",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionStartDate",
                table: "Companies",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Companies",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPricePerEmployee",
                table: "Companies",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "UnifiedNumber",
                table: "Companies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DayName",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ParentCompanyId",
                table: "Companies",
                column: "ParentCompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                table: "Companies",
                column: "ParentCompanyId",
                principalTable: "Companies",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_ParentCompanyId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AllowedEmployees",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AllowedSubAccounts",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NationalAddressShortCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NotificationDaysBeforeExpiry",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ParentCompanyId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "PaymentTerm",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "PricePerEmployee",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ResponsiblePerson",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SubscriptionEndDate",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SubscriptionStartDate",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TaxNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TotalPricePerEmployee",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "UnifiedNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "DayName",
                table: "Attendances");
        }
    }
}
