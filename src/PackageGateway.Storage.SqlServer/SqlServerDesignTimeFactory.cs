using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PackageGateway.Storage.SqlServer;

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        return new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=PackageGatewayDesign;User Id=sa;Password=Design-Only-Password_42;TrustServerCertificate=true",
                options => options.MigrationsAssembly(typeof(SqlServerDesignTimeFactory).Assembly.FullName)).Options);
    }
}