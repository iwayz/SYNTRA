using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntra.Infrastructure.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddSecondDemoTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "TenantId", "ConnectionString", "CreatedAt", "DatabaseName", "DatabaseServer", "Name", "Slug", "Status", "UpdatedAt" },
                values: new object[] { 2, "Data Source=.;Initial Catalog=Syntra_Xyz;Integrated Security=True;TrustServerCertificate=True", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Syntra_Xyz", ".", "Demo Tenant 2", "xyz", "Active", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Tenants",
                keyColumn: "TenantId",
                keyValue: 2);
        }
    }
}
