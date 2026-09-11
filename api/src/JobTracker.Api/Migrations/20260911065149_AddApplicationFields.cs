using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "applied_at",
                table: "applications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_name",
                table: "applications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "job_title",
                table: "applications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "applications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "applications_updated_at_idx",
                table: "applications",
                column: "updated_at",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "applications_updated_at_idx",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "applied_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "company_name",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "job_title",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "location",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "status",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "applications");
        }
    }
}
