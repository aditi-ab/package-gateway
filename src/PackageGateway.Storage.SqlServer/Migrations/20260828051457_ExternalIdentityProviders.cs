using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PackageGateway.Storage.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ExternalIdentityProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "LocalAdministrators",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalIdentitiesJson",
                table: "LocalAdministrators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "RoleGrantsJson",
                table: "LocalAdministrators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "AdminIdentityProviders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminIdentityProviders", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminIdentityProviders");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "ExternalIdentitiesJson",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "RoleGrantsJson",
                table: "LocalAdministrators");
        }
    }
}
