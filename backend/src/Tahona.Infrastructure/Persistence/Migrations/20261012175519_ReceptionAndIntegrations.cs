using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceptionAndIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<DateOnly>>(
                name: "Holidays",
                table: "ClinicSettings",
                type: "date[]",
                nullable: false,
                defaultValueSql: "'{}'"); // existing clinics start with no closed dates

            migrationBuilder.CreateTable(
                name: "ClinicWhatsApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumberId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DisplayPhoneNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    VerifiedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EncryptedAccessToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConnectedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicWhatsApps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicWhatsApps_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicWhatsApps_ClinicId",
                table: "ClinicWhatsApps",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicWhatsApps_PhoneNumberId",
                table: "ClinicWhatsApps",
                column: "PhoneNumberId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicWhatsApps");

            migrationBuilder.DropColumn(
                name: "Holidays",
                table: "ClinicSettings");
        }
    }
}
