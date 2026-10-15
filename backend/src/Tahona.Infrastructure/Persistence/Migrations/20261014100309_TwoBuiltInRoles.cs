using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Clinics now start with two built-in roles, Admin and Dentist, and dentists may record payments.
    /// For clinics that already exist:
    ///  - the built-in Dentist role gains <c>payments.manage</c>;
    ///  - a built-in Receptionist or Accountant role that nobody holds is removed;
    ///  - one that staff still hold becomes an ordinary role of the clinic, so nobody loses access without
    ///    an administrator deciding it. The administrator can then move those people and delete the role.
    /// </summary>
    public partial class TwoBuiltInRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'payments.manage')
                WHERE "IsSystem" AND "Name" = 'Dentist' AND NOT ('payments.manage' = ANY ("Permissions"));

                DELETE FROM "Roles" r
                WHERE r."IsSystem" AND r."Name" IN ('Receptionist', 'Accountant')
                  AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."RoleId" = r."Id");

                UPDATE "Roles" SET "IsSystem" = false
                WHERE "IsSystem" AND "Name" IN ('Receptionist', 'Accountant');
                """);
        }

        /// <inheritdoc />
        /// <remarks>Removed roles are not recreated; new clinics would get them back only from older code.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Roles" SET "Permissions" = array_remove("Permissions", 'payments.manage')
                WHERE "IsSystem" AND "Name" = 'Dentist';

                UPDATE "Roles" SET "IsSystem" = true
                WHERE NOT "IsSystem" AND "Name" IN ('Receptionist', 'Accountant');
                """);
        }
    }
}
