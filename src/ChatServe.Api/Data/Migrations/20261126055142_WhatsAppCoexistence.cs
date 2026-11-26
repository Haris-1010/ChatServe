using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChatServe.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppCoexistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HistoryMessagesImported",
                table: "Restaurants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistorySyncRequestedAt",
                table: "Restaurants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastPhoneReplyAt",
                table: "Restaurants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PauseBotOnPhoneReply",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing restaurants: same default as new ones

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppAccessToken",
                table: "Restaurants",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppBusinessAccountId",
                table: "Restaurants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WhatsAppConnectedAt",
                table: "Restaurants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppMode",
                table: "Restaurants",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "CloudApi"); // existing restaurants keep the classic setup

            migrationBuilder.CreateTable(
                name: "PhoneContacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneContacts_Restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneContacts_RestaurantId_Phone",
                table: "PhoneContacts",
                columns: new[] { "RestaurantId", "Phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneContacts");

            migrationBuilder.DropColumn(
                name: "HistoryMessagesImported",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "HistorySyncRequestedAt",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "LastPhoneReplyAt",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "PauseBotOnPhoneReply",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "WhatsAppAccessToken",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "WhatsAppBusinessAccountId",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "WhatsAppConnectedAt",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "WhatsAppMode",
                table: "Restaurants");
        }
    }
}
