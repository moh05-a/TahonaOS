using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Fee",
                table: "Appointments",
                type: "numeric(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_AppointmentId",
                table: "Invoices",
                column: "AppointmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Appointments_AppointmentId",
                table: "Invoices",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Appointments_AppointmentId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_AppointmentId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Fee",
                table: "Appointments");
        }
    }
}
