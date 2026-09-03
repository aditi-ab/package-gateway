using PackageGateway.ProtocolTests.Common;
using Xunit;

namespace PackageGateway.Npm.Tests;

public sealed class NpmCliAcceptanceTests
{
    [Fact(Timeout = 180_000)]
    public async Task Install_ci_view_transitive_dependency_and_immutable_cache_work_with_real_cli()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = await ProtocolTestGateway.StartAsync(ct);
        var directory = Path.Combine(Path.GetTempPath(), $"npm-cli-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var npm = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm.cmd")
                : "npm";
            await File.WriteAllTextAsync(Path.Combine(directory, "package.json"),
                "{\"name\":\"gateway-client\",\"version\":\"1.0.0\",\"private\":true,\"dependencies\":{\"example\":\"1.0.0\"}}",
                ct);
            var auth = gateway.NpmRegistry.Replace("http:", string.Empty, StringComparison.Ordinal)
                .Replace("https:", string.Empty, StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(directory, ".npmrc"),
                $"registry={gateway.NpmRegistry}\n{auth}:_authToken={gateway.Token}\nalways-auth=true\n", ct);
            var install =
                await ProtocolTestGateway.RunAsync(npm, "install --ignore-scripts --no-audit --no-fund", directory, ct);
            Assert.True(install.ExitCode == 0, install.Combined);
            Assert.True(File.Exists(Path.Combine(directory, "node_modules", "example", "package.json")));
            Assert.True(File.Exists(Path.Combine(directory, "node_modules", "dependency", "package.json")));
            Assert.True(File.Exists(Path.Combine(directory, "node_modules", "@scope", "scoped", "package.json")));
            var view = await ProtocolTestGateway.RunAsync(npm, "view example version --json", directory, ct);
            Assert.True(view.ExitCode == 0, view.Combined);
            Assert.Contains("1.0.0", view.StandardOutput);
            Directory.Delete(Path.Combine(directory, "node_modules"), true);
            var ci = await ProtocolTestGateway.RunAsync(npm, "ci --ignore-scripts --no-audit --no-fund", directory, ct);
            Assert.True(ci.ExitCode == 0, ci.Combined);
            Assert.Equal(1, gateway.ArtifactRequests("npm:example"));
            Assert.Equal(1, gateway.ArtifactRequests("npm:dependency"));
            Assert.Equal(1, gateway.ArtifactRequests("npm:@scope/scoped"));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}