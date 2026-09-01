using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsyncCsvProcessor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobStuckRecoveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryAttempts",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "RecoveryAttempts",
                table: "Jobs");
        }
    }
}
