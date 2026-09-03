using PackageGateway.Api;
using Xunit;

namespace PackageGateway.IntegrationTests;

public sealed class DocumentationPathRewriterTests
{
    [Fact]
    public void ResolveAddsHtmlExtensionWhenGeneratedDocumentationPageExists()
    {
        var resolvedPath = DocumentationPathRewriter.Resolve("/docs/guide/introduction",
            path => path == "docs/guide/introduction.html");

        Assert.Equal("/docs/guide/introduction.html", resolvedPath.Value);
    }

    [Theory]
    [InlineData("/docs/")]
    [InlineData("/docs/assets/app.js")]
    [InlineData("/docs/missing")]
    [InlineData("/admin/")]
    public void ResolveLeavesNonPagePathsUnchanged(string requestPath)
    {
        var resolvedPath = DocumentationPathRewriter.Resolve(requestPath, _ => false);

        Assert.Equal(requestPath, resolvedPath.Value);
    }
}