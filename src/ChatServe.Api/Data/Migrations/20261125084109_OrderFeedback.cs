using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatServe.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrderFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedbackAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackComment",
                table: "Orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FeedbackPromptMessageId",
                table: "Orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_FeedbackAt",
                table: "Orders",
                columns: new[] { "RestaurantId", "FeedbackAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_FeedbackAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FeedbackAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FeedbackComment",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FeedbackPromptMessageId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "Orders");
        }
    }
}
