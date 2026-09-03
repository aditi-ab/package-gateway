using PackageGateway.Domain;
using Xunit;

namespace PackageGateway.NuGet.Tests;

public sealed class NuGetContractTests
{
    [Fact]
    public void NuGet_identity_is_case_insensitive()
    {
        Assert.Equal("newtonsoft.json", PackageIdentity.Normalize("Newtonsoft.Json", PackageType.NuGet));
    }
}