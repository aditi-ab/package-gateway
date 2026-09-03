using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PackageGateway.Domain;
using PackageGateway.Security;
using Xunit;

namespace PackageGateway.UnitTests;

public sealed class ArchiveSecurityTests
{
    [Fact]
    public async Task Zip_traversal_is_a_non_waivable_finding()
    {
        await using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("hostile");
        }

        package.Position = 0;
        var scanner = new ArchivePackageScanner(new SecurityOptions(), [new NoOpMalwareScanner()]);
        var result = await scanner.ScanAsync(PackageType.NuGet, package, CancellationToken.None);
        Assert.Contains(result.Findings, x => x.IsHardBlock && x.Type == "Archive");
    }

    [Fact]
    public async Task Known_malware_digest_is_a_non_waivable_finding()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var options = new SecurityOptions { BlockedSha256Digests = [Convert.ToHexString(SHA256.HashData(bytes))] };
        var findings =
            await new KnownDigestMalwareScanner(options).ScanAsync(PackageType.Npm, new MemoryStream(bytes),
                CancellationToken.None);
        Assert.Contains(findings, x => x.IsHardBlock && x.Type == "Malware");
    }

    [Fact]
    public async Task NuGet_manifest_name_preserves_repository_casing()
    {
        await using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("Newtonsoft.Json.nuspec");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync(
                "<package><metadata><id>Newtonsoft.Json</id><version>13.0.3</version><license type=\"expression\">MIT</license></metadata></package>");
        }

        package.Position = 0;

        var result =
            await new ArchivePackageScanner(new SecurityOptions(), [new NoOpMalwareScanner()]).ScanAsync(
                PackageType.NuGet, package, CancellationToken.None);

        Assert.Equal("Newtonsoft.Json", result.PackageName);
        Assert.Equal("MIT", result.License);
    }

    [Fact]
    public async Task Npm_manifest_name_is_used_as_the_display_name()
    {
        await using var package = new MemoryStream();
        using (var gzip = new GZipStream(package, CompressionLevel.SmallestSize, true))
        using (var tar = new TarWriter(gzip, true))
        {
            var json = Encoding.UTF8.GetBytes("{\"name\":\"@scope/package\",\"version\":\"1.0.0\"}");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "package/package.json")
                { DataStream = new MemoryStream(json) });
        }

        package.Position = 0;

        var result =
            await new ArchivePackageScanner(new SecurityOptions(), [new NoOpMalwareScanner()]).ScanAsync(
                PackageType.Npm, package, CancellationToken.None);

        Assert.Equal("@scope/package", result.PackageName);
    }
}