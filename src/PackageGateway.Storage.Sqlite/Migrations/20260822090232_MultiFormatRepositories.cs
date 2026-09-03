using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PackageGateway.Storage.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MultiFormatRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Packages_RepositoryId_NormalizedName",
                table: "Packages");

            migrationBuilder.AddColumn<string>(
                name: "PackageType",
                table: "Upstreams",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("UPDATE Upstreams SET PackageType = (SELECT PackageType FROM Repositories WHERE Repositories.Id = Upstreams.RepositoryId);");

            migrationBuilder.AlterColumn<string>(
                name: "PackageType",
                table: "Upstreams",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "NuGet",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PackageType",
                table: "Repositories",
                type: "TEXT",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "PackageTypes",
                table: "Policies",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "NuGet,Npm");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_RepositoryId_PackageType_NormalizedName",
                table: "Packages",
                columns: new[] { "RepositoryId", "PackageType", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Packages_RepositoryId_PackageType_NormalizedName",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "PackageType",
                table: "Upstreams");

            migrationBuilder.DropColumn(
                name: "PackageTypes",
                table: "Policies");

            migrationBuilder.Sql("UPDATE Repositories SET PackageType = 'NuGet' WHERE PackageType IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "PackageType",
                table: "Repositories",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Packages_RepositoryId_NormalizedName",
                table: "Packages",
                columns: new[] { "RepositoryId", "NormalizedName" },
                unique: true);
        }
    }
}
