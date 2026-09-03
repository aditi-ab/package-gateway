using PackageGateway.Application;
using PackageGateway.Domain;
using PackageGateway.Security;
using Xunit;

namespace PackageGateway.UnitTests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void Policy_format_targeting_is_explicit()
    {
        var policy = Policy.Create("npm scripts", "NpmInstallScriptPolicy", 1, "{\"action\":\"ManualReview\"}",
            [PackageType.Npm]);
        Assert.True(policy.AppliesTo(PackageType.Npm));
        Assert.False(policy.AppliesTo(PackageType.NuGet));
    }

    [Fact]
    public void Pending_package_cannot_be_delivered()
    {
        var version = PackageVersion.Create(Guid.CreateVersion7(), "1.0.0", Guid.CreateVersion7(),
            "https://example.test/package.nupkg");
        Assert.False(version.CanBeDelivered);
    }

    [Fact]
    public void Approved_package_requires_a_pinned_artifact()
    {
        var version = PackageVersion.Create(Guid.CreateVersion7(), "1.0.0", Guid.CreateVersion7(),
            "https://example.test/package.nupkg");
        version.SetArtifact(new string('a', 64), 42);
        version.BeginScan();
        version.CompleteScan(PackageVersionStatus.Approved, 0, false, SignatureStatus.Valid, false, "MIT");
        Assert.True(version.CanBeDelivered);
    }

    [Fact]
    public void Hard_block_cannot_be_manually_approved()
    {
        var version = PackageVersion.Create(Guid.CreateVersion7(), "1.0.0", Guid.CreateVersion7(),
            "https://example.test/package.nupkg");
        version.SetArtifact(new string('a', 64), 42);
        version.BeginScan();
        version.CompleteScan(PackageVersionStatus.Blocked, 100, true, SignatureStatus.Invalid, false, null);
        Assert.Throws<InvalidOperationException>(() => version.ManuallyApprove());
    }

    [Fact]
    public void Pinned_hash_is_immutable()
    {
        var version = PackageVersion.Create(Guid.CreateVersion7(), "1.0.0", Guid.CreateVersion7(),
            "https://example.test/package.nupkg");
        version.SetArtifact(new string('a', 64), 42);
        Assert.Throws<InvalidOperationException>(() => version.SetArtifact(new string('b', 64), 42));
    }

    [Fact]
    public void Approved_package_can_be_sent_to_manual_review()
    {
        var version = PackageVersion.Create(Guid.CreateVersion7(), "1.0.0", Guid.CreateVersion7(),
            "https://example.test/package.nupkg");
        version.SetArtifact(new string('a', 64), 42);
        version.BeginScan();
        version.CompleteScan(PackageVersionStatus.Approved, 0, false, SignatureStatus.Valid, false, "MIT");
        version.ManuallyRequireReview();
        Assert.Equal(PackageVersionStatus.ManualReview, version.Status);
        Assert.False(version.CanBeDelivered);
    }

    [Fact]
    public async Task Hard_guard_has_highest_policy_precedence()
    {
        var package = Package.Create(Guid.CreateVersion7(), "Example", PackageType.NuGet);
        var version = PackageVersion.Create(package.Id, "1.0.0", Guid.CreateVersion7(),
            "https://example.test/package.nupkg", DateTimeOffset.UtcNow.AddDays(-10));
        var inspection = new PackageInspectionResult(
        [
            new ScanFinding("Integrity", FindingSeverity.Critical, "Mismatch", "Digest mismatch", "test",
                IsHardBlock: true, RiskScore: 100)
        ], 100, false, SignatureStatus.Invalid, "MIT");
        var result = await new PolicyEvaluator([]).EvaluateAsync(
            new PolicyEvaluationContext(package, version, inspection, [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.Equal(PolicyAction.Block, result.FinalAction);
        Assert.True(result.HasHardBlock);
    }

    [Fact]
    public async Task Signature_policy_uses_its_configured_action()
    {
        var package = Package.Create(Guid.CreateVersion7(), "Example", PackageType.NuGet);
        var version = PackageVersion.Create(package.Id, "1.0.0", Guid.CreateVersion7(),
            "https://example.test/package.nupkg");
        var policy = Policy.Create("Signature review", "SignaturePolicy", 1,
            "{\"invalidSignature\":\"ManualReview\",\"unsigned\":\"Warn\"}", [PackageType.NuGet]);
        var inspection = new PackageInspectionResult([], 0, false, SignatureStatus.Invalid, "MIT");

        var result = await new PolicyEvaluator([]).EvaluateAsync(
            new PolicyEvaluationContext(package, version, inspection, [], [policy], DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(PolicyAction.ManualReview, result.FinalAction);
        Assert.False(result.HasHardBlock);
    }

    [Theory]
    [InlineData("Newtonsoft.Json", PackageType.NuGet, "newtonsoft.json")]
    [InlineData("@Scope/Package", PackageType.Npm, "@scope/package")]
    public void Ecosystem_names_are_normalized(string input, PackageType type, string expected)
    {
        Assert.Equal(expected, PackageIdentity.Normalize(input, type));
    }

    [Fact]
    public void Package_display_name_can_change_casing_but_not_identity()
    {
        var package = Package.Create(Guid.CreateVersion7(), "newtonsoft.json", PackageType.NuGet);

        package.UpdateDisplayName("Newtonsoft.Json");

        Assert.Equal("Newtonsoft.Json", package.Name);
        Assert.Equal("newtonsoft.json", package.NormalizedName);
        Assert.Throws<InvalidOperationException>(() => package.UpdateDisplayName("Other.Package"));
    }
}