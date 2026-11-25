using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChatServe.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Organizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DashboardUsers_Restaurants_RestaurantId",
                table: "DashboardUsers");

            migrationBuilder.RenameColumn(
                name: "RestaurantId",
                table: "DashboardUsers",
                newName: "OrganizationId");

            migrationBuilder.RenameIndex(
                name: "IX_DashboardUsers_RestaurantId",
                table: "DashboardUsers",
                newName: "IX_DashboardUsers_OrganizationId");

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Restaurants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            // Existing data: every restaurant becomes its own client (organization) with the same name,
            // and each owner login moves from its restaurant to that restaurant's new organization.
            migrationBuilder.Sql("""
                DO $$
                DECLARE r RECORD; new_id integer;
                BEGIN
                  FOR r IN SELECT "Id", "Name", "CreatedAt" FROM "Restaurants" ORDER BY "Id" LOOP
                    INSERT INTO "Organizations" ("Name", "IsActive", "CreatedAt") VALUES (r."Name", true, r."CreatedAt")
                    RETURNING "Id" INTO new_id;
                    UPDATE "Restaurants" SET "OrganizationId" = new_id WHERE "Id" = r."Id";
                  END LOOP;
                END $$;
                """);
            migrationBuilder.Sql("""
                UPDATE "DashboardUsers" AS u SET "OrganizationId" = r."OrganizationId"
                FROM "Restaurants" AS r WHERE r."Id" = u."OrganizationId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Restaurants_OrganizationId",
                table: "Restaurants",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardUsers_Organizations_OrganizationId",
                table: "DashboardUsers",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Restaurants_Organizations_OrganizationId",
                table: "Restaurants",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DashboardUsers_Organizations_OrganizationId",
                table: "DashboardUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Restaurants_Organizations_OrganizationId",
                table: "Restaurants");

            // Each login goes back to the first restaurant of its organization.
            migrationBuilder.Sql("""
                DELETE FROM "DashboardUsers" AS u
                WHERE NOT EXISTS (SELECT 1 FROM "Restaurants" AS r WHERE r."OrganizationId" = u."OrganizationId");
                UPDATE "DashboardUsers" AS u SET "OrganizationId" =
                  (SELECT MIN(r."Id") FROM "Restaurants" AS r WHERE r."OrganizationId" = u."OrganizationId");
                """);

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Restaurants_OrganizationId",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Restaurants");

            migrationBuilder.RenameColumn(
                name: "OrganizationId",
                table: "DashboardUsers",
                newName: "RestaurantId");

            migrationBuilder.RenameIndex(
                name: "IX_DashboardUsers_OrganizationId",
                table: "DashboardUsers",
                newName: "IX_DashboardUsers_RestaurantId");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardUsers_Restaurants_RestaurantId",
                table: "DashboardUsers",
                column: "RestaurantId",
                principalTable: "Restaurants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
