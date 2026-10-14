using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingAndImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Patients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                table: "Patients",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Onboarding",
                table: "ClinicSettings",
                type: "jsonb",
                nullable: false,
                // Clinics that exist before this migration were already in daily use: their switch is complete.
                defaultValue: "{\"ConfirmedSteps\":[],\"CompletedAt\":\"2026-10-12T00:00:00Z\"}");

            migrationBuilder.AlterColumn<string>(
                name: "SubscriptionPlan",
                table: "Clinics",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidUntil",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Clinics",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStatus",
                table: "Clinics",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Active"); // existing clinics keep full access

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAt",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Appointments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            // The earlier placeholder plan name maps to the standard plan; the seeded sample clinic is marked as a demo.
            migrationBuilder.Sql("UPDATE \"Clinics\" SET \"SubscriptionPlan\" = 'Standard' WHERE \"SubscriptionPlan\" = 'Professional';");
            migrationBuilder.Sql("UPDATE \"Clinics\" SET \"IsDemo\" = TRUE WHERE \"Slug\" = 'smile-line';");

            migrationBuilder.CreateTable(
                name: "ClinicInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClinicName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Plan = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    HeadersJson = table.Column<string>(type: "jsonb", nullable: false),
                    MappingJson = table.Column<string>(type: "jsonb", nullable: true),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommittedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UndoneAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MessagesJson = table.Column<string>(type: "jsonb", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRows_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_SourceSystem_ExternalId",
                table: "Patients",
                columns: new[] { "ClinicId", "SourceSystem", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ImportBatchId",
                table: "Patients",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ImportBatchId",
                table: "Invoices",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ImportBatchId",
                table: "Appointments",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicInvitations_Email",
                table: "ClinicInvitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicInvitations_TokenHash",
                table: "ClinicInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_ClinicId_CreatedAt",
                table: "ImportBatches",
                columns: new[] { "ClinicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_ClinicId_FileHash",
                table: "ImportBatches",
                columns: new[] { "ClinicId", "FileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CreatedById",
                table: "ImportBatches",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_BatchId_RowNumber",
                table: "ImportRows",
                columns: new[] { "BatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_TokenHash",
                table: "UserTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_UserId",
                table: "UserTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicInvitations");

            migrationBuilder.DropTable(
                name: "ImportRows");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_SourceSystem_ExternalId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ImportBatchId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ImportBatchId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ImportBatchId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SourceSystem",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Onboarding",
                table: "ClinicSettings");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "PaidUntil",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Appointments");

            migrationBuilder.AlterColumn<string>(
                name: "SubscriptionPlan",
                table: "Clinics",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}
