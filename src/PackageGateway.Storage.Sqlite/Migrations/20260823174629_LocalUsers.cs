using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PackageGateway.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class LocalUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "LocalAdministrators",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "LocalAdministrators",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "LastLoginAt",
                table: "LocalAdministrators",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "LocalAdministrators",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Roles",
                table: "LocalAdministrators",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "Administrator");

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "LocalAdministrators",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "MIGRATED");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "Roles",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "LocalAdministrators");
        }
    }
}
