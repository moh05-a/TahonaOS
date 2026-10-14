using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledById",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CancelledById",
                table: "Payments",
                column: "CancelledById");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_CancelledById",
                table: "Payments",
                column: "CancelledById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_CancelledById",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CancelledById",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancelledById",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Payments");
        }
    }
}
