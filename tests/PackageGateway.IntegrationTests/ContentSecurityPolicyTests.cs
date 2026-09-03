using PackageGateway.Api;
using Xunit;

namespace PackageGateway.IntegrationTests;

public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void ResolveAllowsVitePressBootstrapScriptsForDocumentation()
    {
        var policy = ContentSecurityPolicy.Resolve("/docs/guide/introduction");

        Assert.Contains("script-src 'self' 'unsafe-inline'", policy);
    }

    [Theory]
    [InlineData("/admin/")]
    [InlineData("/graphql")]
    public void ResolveKeepsStrictScriptPolicyOutsideDocumentation(string requestPath)
    {
        var policy = ContentSecurityPolicy.Resolve(requestPath);

        Assert.Contains("script-src 'self';", policy);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", policy);
    }
}