using System.Net;
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

public sealed class AcquisitionResilienceTests
{
    [Fact]
    public async Task Evaluation_continues_after_initial_wait_and_later_serves_the_local_blob()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(new DelayedScanner(TimeSpan.FromMilliseconds(250)),
            TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(2), ct);
        var first = await fixture.Coordinator.GetOrAcquireAsync(fixture.Request, ct);
        Assert.Equal(ArtifactDeliveryStatus.Pending, first.Status);
        await Task.Delay(400, ct);
        var later = await fixture.Coordinator.GetOrAcquireAsync(fixture.Request, ct);
        Assert.Equal(ArtifactDeliveryStatus.Approved, later.Status);
        Assert.Equal(1, fixture.Handler.RequestCount);
        if (later.Content is not null) await later.Content.DisposeAsync();
    }

    [Fact]
    public async Task Caller_cancellation_does_not_cancel_the_shared_security_evaluation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(new DelayedScanner(TimeSpan.FromMilliseconds(200)),
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), ct);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        caller.CancelAfter(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Coordinator.GetOrAcquireAsync(fixture.Request, caller.Token));
        await Task.Delay(350, ct);
        var later = await fixture.Coordinator.GetOrAcquireAsync(fixture.Request, ct);
        Assert.Equal(ArtifactDeliveryStatus.Approved, later.Status);
        Assert.Equal(1, fixture.Handler.RequestCount);
        if (later.Content is not null) await later.Content.DisposeAsync();
    }

    [Fact]
    public async Task Upstream_byte_mutation_blocks_the_version_and_preserves_the_approved_blob()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(new DelayedScanner(TimeSpan.Zero), TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2), ct);
        var approved = await fixture.Coordinator.GetOrAcquireAsync(fixture.Request, ct);
        Assert.Equal(ArtifactDeliveryStatus.Approved, approved.Status);
        if (approved.Content is not null) await approved.Content.DisposeAsync();
        fixture.Handler.SetContent([9, 9, 9, 9]);
        await fixture.Coordinator.VerifyOriginAsync((await FindAsync(fixture, ct)).Id, ct);
        var blocked = await FindAsync(fixture, ct);
        Assert.Equal(PackageVersionStatus.Blocked, blocked.Status);
        Assert.True(blocked.HasHardBlock);
        await using var scope = fixture.Provider.CreateAsyncScope();
        await using var blob = await scope.ServiceProvider.GetRequiredService<IPackageBlobStore>()
            .OpenReadAsync(blocked.Id, ct);
        Assert.NotNull(blob);
        using var copy = new MemoryStream();
        await blob.CopyToAsync(copy, ct);
        Assert.Equal(new byte[] { 1, 3, 3, 7 }, copy.ToArray());
    }

    private static async Task<PackageVersion> FindAsync(Fixture fixture, CancellationToken ct)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IGatewayStore>()
            .FindPackageVersionAsync(fixture.RepositoryId, PackageType.NuGet, "example", "1.0.0", ct))!.Value.Version;
    }

    [Theory]
    [InlineData(false, ScanResult.Failed)]
    [InlineData(true, ScanResult.TimedOut)]
    public async Task Scanner_failure_or_timeout_fails_closed_into_manual_review(bool timeout,
        ScanResult expectedResult)
    {
        var ct = TestContext.Current.CancellationToken;
        IPackageScanner scanner = timeout ? new DelayedScanner(Timeout.InfiniteTimeSpan) : new ThrowingScanner();
        await using var fixture =
            await Fixture.StartAsync(scanner, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50), ct);
        var delivery = await fixture.Coordinator.GetOrAcquireAsync(fixture.Request, ct);
        Assert.Equal(ArtifactDeliveryStatus.Denied, delivery.Status);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        var persisted =
            await store.FindPackageVersionAsync(fixture.RepositoryId, PackageType.NuGet, "example", "1.0.0", ct);
        Assert.NotNull(persisted);
        Assert.Equal(PackageVersionStatus.ManualReview, persisted.Value.Version.Status);
        var scans = await store.GetScansAsync(persisted.Value.Version.Id, new PageRequest(), ct);
        Assert.Equal(expectedResult, Assert.Single(scans.Items).Result);
    }

    [Fact]
    public async Task Vulnerability_provider_outage_without_cache_withholds_the_artifact()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.StartAsync(new DelayedScanner(TimeSpan.Zero), TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2), ct, new UnavailableVulnerabilityProvider());
        var delivery = await fixture.Coordinator.GetOrAcquireAsync(fixture.Request, ct);
        Assert.Equal(ArtifactDeliveryStatus.Denied, delivery.Status);
        var persisted = await FindAsync(fixture, ct);
        Assert.Equal(PackageVersionStatus.ManualReview, persisted.Status);
        Assert.False(persisted.CanBeDelivered);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private Fixture(SqliteConnection connection, ServiceProvider provider, ArtifactHandler handler,
            Guid repositoryId, ArtifactRequest request)
        {
            this.connection = connection;
            Provider = provider;
            Handler = handler;
            RepositoryId = repositoryId;
            Request = request;
        }

        public ServiceProvider Provider { get; }
        public ArtifactHandler Handler { get; }
        public Guid RepositoryId { get; }
        public ArtifactRequest Request { get; }

        public IPackageAcquisitionCoordinator Coordinator =>
            Provider.GetRequiredService<IPackageAcquisitionCoordinator>();

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await connection.DisposeAsync();
        }

        public static async Task<Fixture> StartAsync(IPackageScanner scanner, TimeSpan wait, TimeSpan timeout,
            CancellationToken ct, IVulnerabilityProvider? vulnerabilityProvider = null)
        {
            var bytes = new byte[] { 1, 3, 3, 7 };
            var handler = new ArtifactHandler(bytes);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(connection);
            services.AddDbContext<GatewayDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
            services.AddScoped<GatewayStore>();
            services.AddScoped<IGatewayStore>(x => x.GetRequiredService<GatewayStore>());
            services.AddSingleton<IPackageBlobStore, TestPackageBlobStore>();
            services.AddSingleton(new SecurityOptions { InitialRequestWait = wait, ScanTimeout = timeout });
            services.AddSingleton(scanner);
            services.AddScoped<IPackagePolicyEvaluator, PolicyEvaluator>();
            services.AddSingleton(vulnerabilityProvider ?? new EmptyVulnerabilityProvider());
            services.AddHttpClient("gateway-upstream").ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddSingleton<IUpstreamClient, FixtureUpstreamClient>();
            services.AddSingleton<IPackageOperationLock, InMemoryPackageOperationLock>();
            services.AddSingleton<IPackageAcquisitionCoordinator, PackageAcquisitionCoordinator>();
            var provider = services.BuildServiceProvider();
            Guid repositoryId;
            Guid upstreamId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
                await db.Database.EnsureCreatedAsync(ct);
                var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
                var repo = Repository.Create("NuGet", "nuget", PackageType.NuGet);
                await store.AddRepositoryAsync(repo, [], ct);
                var upstream = Upstream.Create(repo.Id, "fixture", new Uri("https://fixture.test/v3/index.json"), 0,
                    true);
                await store.AddUpstreamAsync(upstream, ct);
                await store.SaveChangesAsync(ct);
                repositoryId = repo.Id;
                upstreamId = upstream.Id;
            }

            return new Fixture(connection, provider, handler, repositoryId,
                new ArtifactRequest(repositoryId, "nuget", PackageType.NuGet, "Example", "1.0.0", upstreamId,
                    new Uri("https://fixture.test/example.1.0.0.nupkg")));
        }
    }

    private sealed class DelayedScanner(TimeSpan delay) : IPackageScanner
    {
        public bool Supports(PackageType packageType)
        {
            return true;
        }

        public async Task<PackageInspectionResult> ScanAsync(PackageType packageType, Stream artifact,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new PackageInspectionResult([], 0, false, SignatureStatus.Unsigned, "MIT");
        }
    }

    private sealed class ThrowingScanner : IPackageScanner
    {
        public bool Supports(PackageType packageType)
        {
            return true;
        }

        public Task<PackageInspectionResult> ScanAsync(PackageType packageType, Stream artifact,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Fixture scanner failure.");
        }
    }

    public sealed class ArtifactHandler(byte[] initialBytes) : HttpMessageHandler
    {
        private byte[] bytes = initialBytes;
        private int count;
        public int RequestCount => count;

        public void SetContent(byte[] value)
        {
            Volatile.Write(ref bytes, value);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(Volatile.Read(ref bytes)) });
        }
    }

    private sealed class FixtureUpstreamClient : IUpstreamClient
    {
        public PackageType PackageType => PackageType.NuGet;

        public Task<ResolvedArtifact?> ResolveExactAsync(Upstream upstream, string packageName, string version,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ResolvedArtifact?>(new ResolvedArtifact(upstream.Id,
                new Uri("https://fixture.test/example.1.0.0.nupkg")));
        }

        public Task<IReadOnlyList<UpstreamPackageDto>> SearchAsync(Upstream upstream, string query, int take,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<UpstreamPackageDto>>([]);
        }

        public Task<IReadOnlyList<string>> GetVersionsAsync(Upstream upstream, string packageName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
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

    private sealed class UnavailableVulnerabilityProvider : IVulnerabilityProvider
    {
        public string Name => "OSV.dev";

        public Task<IReadOnlyList<Vulnerability>> GetVulnerabilitiesAsync(PackageType packageType, string packageName,
            string version, CancellationToken cancellationToken)
        {
            throw new VulnerabilityProviderUnavailableException(Name, new HttpRequestException("Fixture outage."));
        }
    }
}