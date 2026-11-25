using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatServe.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplyLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonthlyAiBudget",
                table: "Organizations");

            migrationBuilder.AddColumn<int>(
                name: "MonthlyReplyLimit",
                table: "Organizations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsageAlertLevel",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UsageAlertMonth",
                table: "Organizations",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonthlyReplyLimit",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "UsageAlertLevel",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "UsageAlertMonth",
                table: "Organizations");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyAiBudget",
                table: "Organizations",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }
    }
}
