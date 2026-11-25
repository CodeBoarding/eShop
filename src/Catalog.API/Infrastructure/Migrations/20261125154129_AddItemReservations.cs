using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.API.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemReservations",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemReservations", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "ReservationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservedItems",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedItems", x => new { x.OrderId, x.ProductId });
                    table.CheckConstraint("CK_ReservedItems_Units", "\"Units\" > 0");
                    table.ForeignKey(
                        name: "FK_ReservedItems_Catalog_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Catalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservedItems_ItemReservations_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ItemReservations",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemReservations_State_ExpiresAt",
                table: "ItemReservations",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationOutbox_PublishedAt_CreatedAt",
                table: "ReservationOutbox",
                columns: new[] { "PublishedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservedItems_ProductId",
                table: "ReservedItems",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationOutbox");

            migrationBuilder.DropTable(
                name: "ReservedItems");

            migrationBuilder.DropTable(
                name: "ItemReservations");
        }
    }
}
