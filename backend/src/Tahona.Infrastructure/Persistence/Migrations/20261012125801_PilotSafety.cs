using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PilotSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ProviderMessageId",
                table: "Messages");

            migrationBuilder.AddColumn<string>(
                name: "MessagingConsent",
                table: "Patients",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unknown"); // existing patients: consent was never recorded

            migrationBuilder.AddColumn<DateTime>(
                name: "MessagingConsentAt",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessagingConsentSource",
                table: "Patients",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromNumber",
                table: "Messages",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutomated",
                table: "Messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoSendAttempts",
                table: "FollowUps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "QuietHoursEnd",
                table: "ClinicSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "QuietHoursStart",
                table: "ClinicSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(21, 0, 0));

            migrationBuilder.AddColumn<bool>(
                name: "RequireConsentForAutomation",
                table: "ClinicSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderAttempts",
                table: "Appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ProviderMessageId",
                table: "Messages",
                column: "ProviderMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ToNumber_CreatedAt",
                table: "Messages",
                columns: new[] { "ToNumber", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ProviderMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ToNumber_CreatedAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MessagingConsent",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "MessagingConsentAt",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "MessagingConsentSource",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FromNumber",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "IsAutomated",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AutoSendAttempts",
                table: "FollowUps");

            migrationBuilder.DropColumn(
                name: "QuietHoursEnd",
                table: "ClinicSettings");

            migrationBuilder.DropColumn(
                name: "QuietHoursStart",
                table: "ClinicSettings");

            migrationBuilder.DropColumn(
                name: "RequireConsentForAutomation",
                table: "ClinicSettings");

            migrationBuilder.DropColumn(
                name: "ReminderAttempts",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ProviderMessageId",
                table: "Messages",
                column: "ProviderMessageId");
        }
    }
}
