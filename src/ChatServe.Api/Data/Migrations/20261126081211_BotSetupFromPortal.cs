using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatServe.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BotSetupFromPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BotInstructions",
                table: "Restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessDescription",
                table: "Restaurants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessType",
                table: "Restaurants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TakesBookings",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing clients keep orders/bookings on

            migrationBuilder.AddColumn<bool>(
                name: "TakesOrders",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing clients keep orders/bookings on

            migrationBuilder.AddColumn<string>(
                name: "WelcomeMessage",
                table: "Restaurants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BotInstructions",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "BusinessDescription",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "TakesBookings",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "TakesOrders",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "WelcomeMessage",
                table: "Restaurants");
        }
    }
}
