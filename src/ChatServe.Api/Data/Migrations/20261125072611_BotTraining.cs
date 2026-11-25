using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatServe.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BotTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrainingExamples",
                table: "Restaurants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrainingExamples",
                table: "Restaurants");
        }
    }
}
