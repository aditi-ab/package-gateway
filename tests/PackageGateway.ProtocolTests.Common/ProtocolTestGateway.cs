using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PackageGateway.Application;
using PackageGateway.Domain;
using PackageGateway.Infrastructure;
using PackageGateway.Protocols.Npm;
using PackageGateway.Protocols.NuGet;
using PackageGateway.Security;
using PackageGateway.Storage;

namespace PackageGateway.ProtocolTests.Common;

public sealed class ProtocolTestGateway : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, int> artifactRequests;
    private readonly string directory;
    private readonly WebApplication gateway;
    private readonly WebApplication upstream;

    private ProtocolTestGateway(WebApplication upstream, WebApplication gateway, string directory,
        ConcurrentDictionary<string, int> artifactRequests, string baseUrl, string token)
    {
        this.upstream = upstream;
        this.gateway = gateway;
        this.directory = directory;
        this.artifactRequests = artifactRequests;
        BaseUrl = baseUrl;
        Token = token;
    }

    public string BaseUrl { get; }
    public string Token { get; }
    public string NuGetSource => $"{BaseUrl}/nuget/shared/v3/index.json";
    public string NpmRegistry => $"{BaseUrl}/npm/shared/";

    public async ValueTask DisposeAsync()
    {
        await gateway.StopAsync();
        await upstream.StopAsync();
        await gateway.DisposeAsync();
        await upstream.DisposeAsync();
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
        }
    }

    public int ArtifactRequests(string identity)
    {
        return artifactRequests.GetValueOrDefault(identity);
    }

    public static async Task<ProtocolTestGateway> StartAsync(CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"package-gateway-protocol-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(directory);
        var certificate = CreateCertificate();
        var artifacts = CreateArtifacts();
        var requests = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var upstreamBuilder = WebApplication.CreateSlimBuilder();
        upstreamBuilder.Logging.AddConsole();
        upstreamBuilder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        var upstream = upstreamBuilder.Build();
        MapUpstream(upstream, artifacts, requests);
        await upstream.StartAsync(ct);
        var upstreamUrl = Address(upstream);

        var gatewayBuilder = WebApplication.CreateSlimBuilder();
        gatewayBuilder.Logging.AddConsole();
        gatewayBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var services = gatewayBuilder.Services;
        var database = Path.Combine(directory, "gateway.db");
        services.AddLogging();
        services.AddRouting();
        services.AddHttpContextAccessor();
        services.AddDbContext<GatewayDbContext>(options => options.UseSqlite($"Data Source={database}"));
        services.AddScoped<GatewayStore>();
        services.AddScoped<IGatewayStore>(sp => sp.GetRequiredService<GatewayStore>());
        services.AddOptions<BlobStorageOptions>().Configure(options => options.Path = Path.Combine(directory, "blobs"));
        services.AddScoped<FileSystemPackageBlobStore>();
        services.AddScoped<IPackageBlobStore>(sp => sp.GetRequiredService<FileSystemPackageBlobStore>());
        services.AddScoped<IVulnerabilityCacheStore>(sp => sp.GetRequiredService<GatewayStore>());
        var security = new SecurityOptions
            { InitialRequestWait = TimeSpan.FromSeconds(20), ScanTimeout = TimeSpan.FromSeconds(20) };
        services.AddSingleton(security);
        services.AddSingleton<IMalwareScanner, NoOpMalwareScanner>();
        services.AddSingleton<IPackageScanner, ArchivePackageScanner>();
        services.AddScoped<IPackagePolicyEvaluator, PolicyEvaluator>();
        services.AddSingleton<IVulnerabilityProvider, EmptyVulnerabilityProvider>();
        services.AddSingleton<IPackageOperationLock, InMemoryPackageOperationLock>();
        services.AddSingleton<IPackageAcquisitionCoordinator, PackageAcquisitionCoordinator>();
        services.AddScoped<IUpstreamResolver, UpstreamResolver>();
        services.AddOptions<GatewayInfrastructureOptions>()
            .Configure(options => options.TokenPepper = "protocol-test-pepper-0123456789abcdef");
        services.AddScoped<IAccessTokenService, AccessTokenService>();
        services.AddNuGetProtocol();
        services.AddNpmProtocol();
        services.AddHttpClient<NuGetUpstreamService>()
            .ConfigurePrimaryHttpMessageHandler((handler, _) => TrustCertificate(handler, certificate));
        services.AddHttpClient<NpmUpstreamService>()
            .ConfigurePrimaryHttpMessageHandler((handler, _) => TrustCertificate(handler, certificate));
        services.AddHttpClient("gateway-upstream")
            .ConfigurePrimaryHttpMessageHandler(() => TrustedHandler(certificate));
        services.AddRateLimiter(options => options.AddFixedWindowLimiter("package", limiter =>
        {
            limiter.PermitLimit = 10_000;
            limiter.Window = TimeSpan.FromMinutes(1);
        }));
        var gateway = gatewayBuilder.Build();
        gateway.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception exception)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(exception.ToString());
            }
        });
        gateway.UseRateLimiter();
        gateway.MapNuGetProtocol();
        gateway.MapNpmProtocol();
        await gateway.StartAsync(ct);
        var gatewayUrl = Address(gateway);

        string token;
        await using (var scope = gateway.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
            var repository = Repository.Create("Multi-format protocol fixture", "shared");
            await store.AddRepositoryAsync(repository, [], ct);
            await store.AddUpstreamAsync(
                Upstream.Create(repository.Id, "NuGet fixture", new Uri($"{upstreamUrl}/nuget/v3/index.json"), 0, true),
                ct);
            await store.AddUpstreamAsync(
                Upstream.Create(repository.Id, "npm fixture", new Uri($"{upstreamUrl}/npm"), 0, true, PackageType.Npm),
                ct);
            await store.SaveChangesAsync(ct);
            var tokenService = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
            token = (await tokenService.CreateAsync("protocol", "test", ["repository:read"],
                DateTimeOffset.UtcNow.AddHours(1), ct)).Secret;
        }

        return new ProtocolTestGateway(upstream, gateway, directory, requests, gatewayUrl, token);
    }

    public static async Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static void MapUpstream(WebApplication app, IReadOnlyDictionary<string, byte[]> artifacts,
        ConcurrentDictionary<string, int> requests)
    {
        app.MapGet("/nuget/v3/index.json",
            (HttpContext c) => CompressedJson(c,
                new
                {
                    version = "3.0.0",
                    resources = new[]
                    {
                        Resource(Absolute(c, "/nuget/flat/"), "PackageBaseAddress/3.0.0"),
                        Resource(Absolute(c, "/nuget/registration/"), "RegistrationsBaseUrl/3.6.0"),
                        Resource(Absolute(c, "/nuget/search"), "SearchQueryService/3.5.0")
                    }
                }));
        app.MapGet("/nuget/flat/{id}/index.json",
            (string id) => artifacts.ContainsKey($"nuget:{id.ToLowerInvariant()}")
                ? Results.Json(new { versions = new[] { "1.0.0" } })
                : Results.NotFound());
        app.MapGet("/nuget/flat/{id}/{version}/{file}",
            (string id, string version, string file) => Artifact($"nuget:{id.ToLowerInvariant()}",
                "application/octet-stream", artifacts, requests));
        app.MapGet("/nuget/registration/{id}/index.json",
            (HttpContext c, string id) => artifacts.ContainsKey($"nuget:{id.ToLowerInvariant()}")
                ? Results.Json(Registration(c, id))
                : Results.NotFound());
        app.MapGet("/nuget/search",
            () => Results.Json(new { totalHits = 2, data = new[] { Search("Example"), Search("Dependency") } }));
        app.MapGet("/npm/{**path}", (HttpContext c, string path) =>
        {
            var decoded = Uri.UnescapeDataString(path).Trim('/');
            var marker = decoded.IndexOf("/-/", StringComparison.Ordinal);
            if (marker >= 0) return Artifact($"npm:{decoded[..marker]}", "application/gzip", artifacts, requests);
            return artifacts.TryGetValue($"npm:{decoded}", out var bytes)
                ? CompressedJson(c, NpmMetadata(c, decoded, bytes))
                : Results.NotFound();
        });
    }

    private static IResult CompressedJson(HttpContext context, object value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true))
        {
            JsonSerializer.Serialize(gzip, value);
        }

        context.Response.Headers.ContentEncoding = "gzip";
        return Results.Bytes(output.ToArray(), "application/json");
    }

    private static IResult Artifact(string key, string contentType, IReadOnlyDictionary<string, byte[]> artifacts,
        ConcurrentDictionary<string, int> requests)
    {
        if (!artifacts.TryGetValue(key, out var bytes)) return Results.NotFound();
        requests.AddOrUpdate(key, 1, (_, count) => count + 1);
        return Results.Bytes(bytes, contentType);
    }

    private static object Resource(string id, string type)
    {
        return new Dictionary<string, object> { ["@id"] = id, ["@type"] = type };
    }

    private static object Search(string id)
    {
        return new
        {
            id, version = "1.0.0", description = $"{id} fixture",
            versions = new[] { new { version = "1.0.0", downloads = 0 } }
        };
    }

    private static object Registration(HttpContext context, string id)
    {
        var lower = id.ToLowerInvariant();
        var content = Absolute(context, $"/nuget/flat/{lower}/1.0.0/{lower}.1.0.0.nupkg");
        return new
        {
            count = 1,
            items = new[]
            {
                new
                {
                    count = 1, lower = "1.0.0", upper = "1.0.0",
                    items = new[]
                    {
                        new
                        {
                            packageContent = content,
                            catalogEntry = new
                                { id, version = "1.0.0", published = "2020-01-01T00:00:00Z", listed = true }
                        }
                    }
                }
            }
        };
    }

    private static object NpmMetadata(HttpContext context, string name, byte[] bytes)
    {
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(bytes));
        var dependencies = name == "example"
            ? new Dictionary<string, string> { ["dependency"] = "1.0.0", ["@scope/scoped"] = "1.0.0" }
            : new Dictionary<string, string>();
        var fileName = name.Split('/')[^1];
        return new Dictionary<string, object>
        {
            ["name"] = name,
            ["versions"] = new Dictionary<string, object>
            {
                ["1.0.0"] = new
                {
                    name, version = "1.0.0", license = "MIT", dependencies,
                    dist = new { tarball = Absolute(context, $"/npm/{name}/-/{fileName}-1.0.0.tgz"), integrity }
                }
            },
            ["dist-tags"] = new Dictionary<string, string> { ["latest"] = "1.0.0" },
            ["time"] = new Dictionary<string, string> { ["1.0.0"] = "2020-01-01T00:00:00Z" }
        };
    }

    private static IReadOnlyDictionary<string, byte[]> CreateArtifacts()
    {
        return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["nuget:example"] = NuGetPackage("Example", "Dependency"),
            ["nuget:dependency"] = NuGetPackage("Dependency", null),
            ["npm:example"] = NpmPackage("example",
                new Dictionary<string, string> { ["dependency"] = "1.0.0", ["@scope/scoped"] = "1.0.0" }),
            ["npm:dependency"] = NpmPackage("dependency", new Dictionary<string, string>()),
            ["npm:@scope/scoped"] = NpmPackage("@scope/scoped", new Dictionary<string, string>())
        };
    }

    private static byte[] NuGetPackage(string id, string? dependency)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry($"{id}.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            var dependencies = dependency is null
                ? ""
                : $"<dependencies><group targetFramework=\"net10.0\"><dependency id=\"{dependency}\" version=\"[1.0.0]\" /></group></dependencies>";
            writer.Write(
                $"<package><metadata><id>{id}</id><version>1.0.0</version><authors>Fixture</authors><description>Fixture</description><license type=\"expression\">MIT</license>{dependencies}</metadata></package>");
        }

        return output.ToArray();
    }

    private static byte[] NpmPackage(string name, IReadOnlyDictionary<string, string> dependencies)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true))
        using (var tar = new TarWriter(gzip, true))
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(new
                { name, version = "1.0.0", license = "MIT", dependencies });
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "package/package.json")
                { DataStream = new MemoryStream(json) };
            tar.WriteEntry(entry);
            var index = new PaxTarEntry(TarEntryType.RegularFile, "package/index.js")
                { DataStream = new MemoryStream(Encoding.UTF8.GetBytes($"module.exports = '{name}';\n")) };
            tar.WriteEntry(index);
        }

        return output.ToArray();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var purposes = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var generated =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12, "fixture"), "fixture",
            X509KeyStorageFlags.Exportable);
    }

    private static HttpMessageHandler TrustedHandler(X509Certificate2 expected)
    {
        var handler = new HttpClientHandler();
        TrustCertificate(handler, expected);
        return handler;
    }

    private static void TrustCertificate(HttpMessageHandler handler, X509Certificate2 expected)
    {
        if (handler is not HttpClientHandler httpHandler)
            throw new InvalidOperationException(
                $"Expected {nameof(HttpClientHandler)}, received {handler.GetType().Name}.");
        httpHandler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            errors == SslPolicyErrors.None || string.Equals(certificate?.GetCertHashString(),
                expected.GetCertHashString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Absolute(HttpContext context, string path)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}{path}";
    }

    private static string Address(WebApplication app)
    {
        return app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()
            .TrimEnd('/');
    }

    private sealed class EmptyVulnerabilityProvider : IVulnerabilityProvider
    {
        public string Name => "fixture";

        public Task<IReadOnlyList<Vulnerability>> GetVulnerabilitiesAsync(PackageType packageType, string packageName,
            string version, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Vulnerability>>([]);
        }
    }
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + Environment.NewLine + StandardError;
}