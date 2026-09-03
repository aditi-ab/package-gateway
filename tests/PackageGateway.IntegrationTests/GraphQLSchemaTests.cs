using System.Security.Cryptography;
using System.Text;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Api;
using Xunit;

namespace PackageGateway.IntegrationTests;

public sealed class GraphQLSchemaTests
{
    [Fact]
    public async Task Management_schema_contains_ui_contract_and_builds_eagerly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthorization();
        services.AddGraphQLServer().AddAuthorization().AddQueryType<Query>().AddMutationType<Mutation>()
            .AddTypeExtension<PackageVersionGraphQL>().AddTypeExtension<RepositoryGraphQL>();
        await using var provider = services.BuildServiceProvider();
        var executor = await provider.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var schema = executor.Schema.ToString();
        Assert.Contains("repositories(", schema);
        Assert.Contains("quarantinedPackages(", schema);
        Assert.Contains("pageInfo", schema);
        Assert.Contains("createRepository(", schema);
        Assert.Contains("waivePackageVersion(", schema);
        Assert.Contains("requirePackageVersionReview(", schema);
        Assert.Contains("removePackageVersion(", schema);
        Assert.Contains("addPackageVersion(", schema);
        Assert.Contains("upstreamPackages(", schema);
        Assert.Contains("upstreamPackageVersions(", schema);
        Assert.Contains("createAccessToken(", schema);
        Assert.Contains("createLocalUser(", schema);
        Assert.Contains("localUsers", schema);
        Assert.Contains("auditEventEntityTypes", schema);
        Assert.Contains("entraConnection", schema);
        Assert.Contains("updateEntraConnection(", schema);
        Assert.Contains("package: PackageDto", schema);
        Assert.Contains("NU_GET", schema);
        Assert.Contains("packageTypes: [PackageType!]!", schema);
        Assert.Contains("packageType: PackageType!", schema);
        var snapshot =
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(schema.Replace("\r\n", "\n", StringComparison.Ordinal))));
        Assert.Contains("reason: \"Use packageTypes. Repository package type is retained only for compatibility.\"",
            schema);
        Assert.Equal("e49e7b1d28901ae6d6a28a5d2aa687dafcaaa496f0620acfa133715bab28962a", snapshot);
    }
}
