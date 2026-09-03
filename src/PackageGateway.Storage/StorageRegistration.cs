using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Application;

namespace PackageGateway.Storage;

public static class StorageRegistration
{
    public static IServiceCollection AddGatewayStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                x => x.Provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) ||
                     x.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase),
                "Database:Provider must be Sqlite or SqlServer.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.ConnectionString), "Database:ConnectionString is required.")
            .ValidateOnStart();
        services.AddOptions<BlobStorageOptions>().Bind(configuration.GetSection(BlobStorageOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.Path), "BlobStorage:Path is required.")
            .ValidateOnStart();
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration["Database:ConnectionString"] ?? "Data Source=/data/packagegateway.db";
        services.AddDbContext<GatewayDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure();
                    sql.MigrationsAssembly("PackageGateway.Storage.SqlServer");
                });
            else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString,
                    sqlite => sqlite.MigrationsAssembly("PackageGateway.Storage.Sqlite"));
            else throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        });
        services.AddScoped<GatewayStore>();
        services.AddScoped<IGatewayStore>(sp => sp.GetRequiredService<GatewayStore>());
        services.AddScoped<FileSystemPackageBlobStore>();
        services.AddScoped<IPackageBlobStore>(sp => sp.GetRequiredService<FileSystemPackageBlobStore>());
        services.AddScoped<ILegacyPackageBlobMigrator>(sp => sp.GetRequiredService<FileSystemPackageBlobStore>());
        services.AddScoped<IVulnerabilityCacheStore>(sp => sp.GetRequiredService<GatewayStore>());
        return services;
    }
}