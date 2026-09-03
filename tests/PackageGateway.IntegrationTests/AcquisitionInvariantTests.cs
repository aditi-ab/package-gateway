using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Application;
using PackageGateway.Domain;
using PackageGateway.Infrastructure;
using PackageGateway.Security;
using PackageGateway.Storage;
using Xunit;

namespace PackageGateway.IntegrationTests;

public sealed class AcquisitionInvariantTests
{
    [Fact]
    public async Task Vulnerability_findings_contribute_to_the_persisted_risk_score()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = await CreatePackageAsync(ct);
        var handler = new ArtifactHandler(bytes);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(connection);
        services.AddDbContext<GatewayDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        services.AddScoped<GatewayStore>();
        services.AddScoped<IGatewayStore>(provider => provider.GetRequiredService<GatewayStore>());
        services.AddSingleton<IPackageBlobStore, TestPackageBlobStore>();
        var security = new SecurityOptions
            { InitialRequestWait = TimeSpan.FromSeconds(10), ScanTimeout = TimeSpan.FromSeconds(10) };
        services.AddSingleton(security);
        services.AddSingleton<IMalwareScanner, NoOpMalwareScanner>();
        services.AddSingleton<IPackageScanner, ArchivePackageScanner>();
        services.AddScoped<IPackagePolicyEvaluator, PolicyEvaluator>();
        services.AddSingleton<IVulnerabilityProvider, HighVulnerabilityProvider>();
        services.AddHttpClient("gateway-upstream").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IPackageOperationLock, InMemoryPackageOperationLock>();
        services.AddSingleton<IPackageAcquisitionCoordinator, PackageAcquisitionCoordinator>();
        await using var provider = services.BuildServiceProvider();
        Guid repositoryId;
        Guid upstreamId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
            var repository = Repository.Create("NuGet", "nuget", PackageType.NuGet);
            await store.AddRepositoryAsync(repository, BalancedPolicyFactory.CreateFor(PackageType.NuGet), ct);
            var upstream = Upstream.Create(repository.Id, "fixture", new Uri("https://fixture.test/v3/index.json"), 0,
                true);
            await store.AddUpstreamAsync(upstream, ct);
            await store.SaveChangesAsync(ct);
            repositoryId = repository.Id;
            upstreamId = upstream.Id;
        }

        var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var request = new ArtifactRequest(repositoryId, "nuget", PackageType.NuGet, "example", "1.0.0", upstreamId,
            new Uri("https://fixture.test/example.1.0.0.nupkg"), DateTimeOffset.UtcNow.AddDays(-10), expected);

        var delivery = await provider.GetRequiredService<IPackageAcquisitionCoordinator>()
            .GetOrAcquireAsync(request, ct);

        Assert.Equal(ArtifactDeliveryStatus.Denied, delivery.Status);
        await using var verificationScope = provider.CreateAsyncScope();
        var persisted = await verificationScope.ServiceProvider.GetRequiredService<IGatewayStore>()
            .FindPackageVersionAsync(repositoryId, PackageType.NuGet, "example", "1.0.0", ct);
        Assert.NotNull(persisted);
        Assert.Equal(70, persisted.Value.Version.RiskScore);
        Assert.Equal("Example", persisted.Value.Package.Name);
    }

    [Theory]
    [InlineData(true, ArtifactDeliveryStatus.Approved, PackageVersionStatus.Approved)]
    [InlineData(false, ArtifactDeliveryStatus.Denied, PackageVersionStatus.Blocked)]
    public async Task Artifact_is_delivered_only_after_integrity_and_policy_approval(bool matchingDigest,
        ArtifactDeliveryStatus deliveryStatus, PackageVersionStatus persistedStatus)
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = await CreatePackageAsync(ct);
        var handler = new ArtifactHandler(bytes);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(connection);
        services.AddDbContext<GatewayDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        services.AddScoped<GatewayStore>();
        services.AddScoped<IGatewayStore>(provider => provider.GetRequiredService<GatewayStore>());
        services.AddSingleton<IPackageBlobStore, TestPackageBlobStore>();
        var security = new SecurityOptions
            { InitialRequestWait = TimeSpan.FromSeconds(10), ScanTimeout = TimeSpan.FromSeconds(10) };
        services.AddSingleton(security);
        services.AddSingleton<IMalwareScanner, NoOpMalwareScanner>();
        services.AddSingleton<IPackageScanner, ArchivePackageScanner>();
        services.AddScoped<IPackagePolicyEvaluator, PolicyEvaluator>();
        services.AddSingleton<IVulnerabilityProvider, EmptyVulnerabilityProvider>();
        services.AddHttpClient("gateway-upstream").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IPackageOperationLock, InMemoryPackageOperationLock>();
        services.AddSingleton<IPackageAcquisitionCoordinator, PackageAcquisitionCoordinator>();
        await using var provider = services.BuildServiceProvider();
        Guid repositoryId;
        Guid upstreamId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
            var repository = Repository.Create("NuGet", "nuget", PackageType.NuGet);
            await store.AddRepositoryAsync(repository, BalancedPolicyFactory.CreateFor(PackageType.NuGet), ct);
            var upstream = Upstream.Create(repository.Id, "fixture", new Uri("https://fixture.test/v3/index.json"), 0,
                true);
            await store.AddUpstreamAsync(upstream, ct);
            await store.SaveChangesAsync(ct);
            repositoryId = repository.Id;
            upstreamId = upstream.Id;
        }

        var expected = matchingDigest ? Convert.ToHexStringLower(SHA256.HashData(bytes)) : new string('0', 64);
        var request = new ArtifactRequest(repositoryId, "nuget", PackageType.NuGet, "Example", "1.0.0", upstreamId,
            new Uri("https://fixture.test/example.1.0.0.nupkg"), DateTimeOffset.UtcNow.AddDays(-10), expected);
        var coordinator = provider.GetRequiredService<IPackageAcquisitionCoordinator>();
        var deliveries = await Task.WhenAll(coordinator.GetOrAcquireAsync(request, ct),
            coordinator.GetOrAcquireAsync(request, ct));
        Assert.All(deliveries, result => Assert.Equal(deliveryStatus, result.Status));
        var later = await coordinator.GetOrAcquireAsync(request, ct);
        Assert.Equal(deliveryStatus, later.Status);
        Assert.Equal(1, handler.RequestCount);
        await using var verificationScope = provider.CreateAsyncScope();
        var persisted = await verificationScope.ServiceProvider.GetRequiredService<IGatewayStore>()
            .FindPackageVersionAsync(repositoryId, PackageType.NuGet, "example", "1.0.0", ct);
        Assert.NotNull(persisted);
        Assert.Equal(persistedStatus, persisted.Value.Version.Status);
        Assert.Equal(matchingDigest, persisted.Value.Version.CanBeDelivered);
        foreach (var delivery in deliveries.Append(later))
            if (delivery.Content is not null)
                await delivery.Content.DisposeAsync();
    }

    private static async Task<byte[]> CreatePackageAsync(CancellationToken ct)
    {
        await using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, true))
        {
            var nuspec = archive.CreateEntry("Example.nuspec");
            await using var writer = new StreamWriter(nuspec.Open());
            await writer.WriteAsync(
                "<package><metadata><id>Example</id><version>1.0.0</version><license type=\"expression\">MIT</license></metadata></package>"
                    .AsMemory(), ct);
        }

        return package.ToArray();
    }

    private sealed class ArtifactHandler(byte[] content) : HttpMessageHandler
    {
        private int requestCount;
        public int RequestCount => requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(content) });
        }
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

    private sealed class HighVulnerabilityProvider : IVulnerabilityProvider
    {
        public string Name => "fixture";

        public Task<IReadOnlyList<Vulnerability>> GetVulnerabilitiesAsync(PackageType packageType, string packageName,
            string version, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Vulnerability>>([
                new Vulnerability("GHSA-fixture", FindingSeverity.High, 7.5, "Fixture vulnerability", null)
            ]);
        }
    }
}