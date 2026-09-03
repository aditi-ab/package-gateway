using System.Text.Json.Nodes;
using PackageGateway.Domain;
using PackageGateway.Protocols.Npm;
using Xunit;

namespace PackageGateway.Npm.Tests;

public sealed class NpmContractTests
{
    [Fact]
    public void Scoped_npm_identity_is_case_insensitive()
    {
        Assert.Equal("@scope/package", PackageIdentity.Normalize("@Scope/Package", PackageType.Npm));
    }

    [Fact]
    public void Tarball_urls_are_rewritten_through_the_gateway()
    {
        var metadata =
            JsonNode.Parse(
                """{"versions":{"1.0.0":{"dist":{"tarball":"https://registry.npmjs.org/@scope/package/-/package-1.0.0.tgz"}}}}""")
            !;
        NpmUrlRewriter.RewriteTarballs(metadata, "@scope/package", "https://gateway.test/npm/public");
        var url = metadata["versions"]!["1.0.0"]!["dist"]!["tarball"]!.GetValue<string>();
        Assert.StartsWith("https://gateway.test/npm/public/", url);
        Assert.Contains("/-/1.0.0/", url);
        Assert.DoesNotContain("registry.npmjs.org", url);
    }
}