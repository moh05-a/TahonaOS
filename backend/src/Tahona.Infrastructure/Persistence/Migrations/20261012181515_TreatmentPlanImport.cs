using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TreatmentPlanImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "TreatmentPlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "TreatmentPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_ImportBatchId",
                table: "TreatmentPlans",
                column: "ImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TreatmentPlans_ImportBatchId",
                table: "TreatmentPlans");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "TreatmentPlans");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "TreatmentPlans");
        }
    }
}
