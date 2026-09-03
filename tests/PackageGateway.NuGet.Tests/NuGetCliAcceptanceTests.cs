using System.Net.Http.Headers;
using System.Text;
using PackageGateway.ProtocolTests.Common;
using Xunit;

namespace PackageGateway.NuGet.Tests;

public sealed class NuGetCliAcceptanceTests
{
    [Fact(Timeout = 180_000)]
    public async Task Restore_search_head_transitive_dependency_and_immutable_cache_work_with_real_cli()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = await ProtocolTestGateway.StartAsync(ct);
        var directory = Path.Combine(Path.GetTempPath(), $"nuget-cli-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "fixture.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Example\" Version=\"1.0.0\" /></ItemGroup></Project>",
                ct);
            var config =
                $"<configuration><packageSources><clear /><add key=\"gateway\" value=\"{gateway.NuGetSource}\" allowInsecureConnections=\"true\" /></packageSources><packageSourceCredentials><gateway><add key=\"Username\" value=\"gateway\" /><add key=\"ClearTextPassword\" value=\"{gateway.Token}\" /></gateway></packageSourceCredentials><config><add key=\"globalPackagesFolder\" value=\"{Path.Combine(directory, "packages")}\" /></config></configuration>";
            await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.Config"), config, ct);
            using (var probe = new HttpClient())
            {
                probe.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"gateway:{gateway.Token}")));
                using var result =
                    await probe.GetAsync($"{gateway.BaseUrl}/nuget/shared/v3/flatcontainer/example/index.json", ct);
                if (!result.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"Probe returned {(int)result.StatusCode}: {await result.Content.ReadAsStringAsync(ct)}");
            }

            var first = await ProtocolTestGateway.RunAsync("dotnet",
                "restore fixture.csproj --configfile NuGet.Config --no-cache", directory, ct);
            Assert.True(first.ExitCode == 0, first.Combined);
            Assert.True(Directory.Exists(Path.Combine(directory, "packages", "example", "1.0.0")));
            Assert.True(Directory.Exists(Path.Combine(directory, "packages", "dependency", "1.0.0")));
            var add = await ProtocolTestGateway.RunAsync("dotnet",
                "add fixture.csproj package Dependency --version 1.0.0 --source gateway", directory, ct);
            Assert.True(add.ExitCode == 0, add.Combined);
            var search = await ProtocolTestGateway.RunAsync("dotnet",
                "package search Example --configfile NuGet.Config --take 5", directory, ct);
            Assert.True(search.ExitCode == 0, search.Combined);
            if (!search.Combined.Contains("Example", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(search.Combined);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"gateway:{gateway.Token}")));
            using var head = new HttpRequestMessage(HttpMethod.Head,
                $"{gateway.BaseUrl}/nuget/shared/v3/flatcontainer/example/1.0.0/example.1.0.0.nupkg");
            using var response = await http.SendAsync(head, ct);
            Assert.True(response.IsSuccessStatusCode);
            Assert.True(response.Content.Headers.ContentLength > 0);
            Directory.Delete(Path.Combine(directory, "packages"), true);
            var second = await ProtocolTestGateway.RunAsync("dotnet",
                "restore fixture.csproj --configfile NuGet.Config --no-cache --force", directory, ct);
            Assert.True(second.ExitCode == 0, second.Combined);
            Assert.Equal(1, gateway.ArtifactRequests("nuget:example"));
            Assert.Equal(1, gateway.ArtifactRequests("nuget:dependency"));
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