using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PackageGateway.Storage.Sqlite;

public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        return new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlite("Data Source=packagegateway.design.db",
                options => options.MigrationsAssembly(typeof(SqliteDesignTimeFactory).Assembly.FullName)).Options);
    }
}