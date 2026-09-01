using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsyncCsvProcessor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JobPriorityAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "Jobs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Jobs");
        }
    }
}
