using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PackageGateway.Application;
using PackageGateway.Domain;
using PackageGateway.Security;

namespace PackageGateway.Infrastructure;

public sealed record JobStatus(
    string Name,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    bool Healthy,
    string? Detail);

public sealed class JobHealthRegistry
{
    private readonly ConcurrentDictionary<string, JobStatus> statuses = new(StringComparer.Ordinal);

    public IReadOnlyList<JobStatus> GetAll()
    {
        return statuses.Values.OrderBy(x => x.Name).ToArray();
    }

    public void Started(string name)
    {
        statuses.AddOrUpdate(name, new JobStatus(name, DateTimeOffset.UtcNow, null, true, null),
            (_, old) => old with { LastStartedAt = DateTimeOffset.UtcNow, Detail = null });
    }

    public void Completed(string name)
    {
        statuses.AddOrUpdate(name, new JobStatus(name, null, DateTimeOffset.UtcNow, true, null),
            (_, old) => old with { LastCompletedAt = DateTimeOffset.UtcNow, Healthy = true, Detail = null });
    }

    public void Failed(string name, string detail)
    {
        statuses.AddOrUpdate(name, new JobStatus(name, null, DateTimeOffset.UtcNow, false, detail),
            (_, old) => old with { LastCompletedAt = DateTimeOffset.UtcNow, Healthy = false, Detail = detail });
    }
}

public sealed class BackgroundJobRunner(
    IEnumerable<IBackgroundJob> jobs,
    IBackgroundJobLeaseProvider leases,
    IOptions<GatewayInfrastructureOptions> options,
    JobHealthRegistry health,
    ILogger<BackgroundJobRunner> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(jobs.Select(job => RunJobAsync(job, stoppingToken)));
    }

    private async Task RunJobAsync(IBackgroundJob job, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        while (!ct.IsCancellationRequested)
        {
            await using var lease =
                await leases.TryAcquireAsync(job.Name, options.Value.BackgroundJobLeaseDuration, ct);
            if (lease is null)
            {
                await Task.Delay(job.Interval, ct);
                continue;
            }

            health.Started(job.Name);
            try
            {
                await job.ExecuteAsync(ct);
                await lease.CompleteAsync(ct);
                GatewayDiagnostics.BackgroundJobOutcomes.Add(1, new KeyValuePair<string, object?>("job", job.Name),
                    new KeyValuePair<string, object?>("outcome", "succeeded"));
                health.Completed(job.Name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await lease.FailAsync(ex.Message, CancellationToken.None);
                GatewayDiagnostics.BackgroundJobOutcomes.Add(1, new KeyValuePair<string, object?>("job", job.Name),
                    new KeyValuePair<string, object?>("outcome", "failed"));
                health.Failed(job.Name, ex.Message);
                logger.LogError(ex, "Background job {Job} failed", job.Name);
            }

            await Task.Delay(job.Interval, ct);
        }
    }
}

public sealed class PendingPackageScanJob(
    IServiceScopeFactory scopes,
    IPackageAcquisitionCoordinator coordinator,
    SecurityOptions security) : IBackgroundJob
{
    public string Name => "PendingPackageScan";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        var pending = (await BackgroundJobPages.VersionsAsync(store, PackageVersionStatus.Pending, ct)).ToList();
        var staleCutoff = DateTimeOffset.UtcNow - security.ScanTimeout - TimeSpan.FromMinutes(1);
        foreach (var snapshot in (await BackgroundJobPages.VersionsAsync(store, PackageVersionStatus.Scanning, ct))
                 .Where(x => x.UpdatedAt < staleCutoff))
        {
            var tracked = await store.FindPackageVersionByIdAsync(snapshot.Id, ct);
            if (tracked is null || tracked.Status != PackageVersionStatus.Scanning) continue;
            tracked.QueueRescan();
            pending.Add(tracked);
            await store.AddAuditAsync(
                AuditEvent.Create("system", "AbandonedScanRecovered", nameof(PackageVersion), tracked.Id.ToString(),
                    "A stale scanning state was recovered and queued."), ct);
        }

        await store.SaveChangesAsync(ct);
        foreach (var version in pending)
        {
            var package = await store.FindPackageAsync(version.PackageId, ct);
            if (package is null) continue;
            var repository = await store.FindRepositoryAsync(package.RepositoryId, ct);
            if (repository is null) continue;
            await coordinator.GetOrAcquireAsync(
                new ArtifactRequest(repository.Id, repository.Slug, package.PackageType, package.Name, version.Version,
                    version.UpstreamId, new Uri(version.ArtifactUrl), version.PublishedAt, version.ExpectedSha256,
                    version.ExpectedIntegrity), ct);
        }
    }
}

public sealed class VulnerabilityRescanJob(IServiceScopeFactory scopes, IOptions<GatewayInfrastructureOptions> options)
    : IBackgroundJob
{
    public string Name => "VulnerabilityRescan";
    public TimeSpan Interval => options.Value.VulnerabilityRescanInterval;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        var approved = await BackgroundJobPages.VersionsAsync(store, PackageVersionStatus.Approved, ct);
        var cutoff = DateTimeOffset.UtcNow - Interval;
        foreach (var item in approved.Where(x => x.LastScannedAt is null || x.LastScannedAt < cutoff))
        {
            var tracked = await store.FindPackageVersionByIdAsync(item.Id, ct);
            if (tracked is null) continue;
            tracked.QueueRescan();
            await store.AddAuditAsync(
                AuditEvent.Create("system", "VulnerabilityRescanQueued", nameof(PackageVersion), tracked.Id.ToString(),
                    "Scheduled vulnerability rescan queued."), ct);
        }

        await store.SaveChangesAsync(ct);
    }
}

public sealed class ExpiredApprovalJob(IServiceScopeFactory scopes) : IBackgroundJob
{
    public string Name => "ExpiredApproval";
    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        foreach (var id in await store.GetExpiredApprovalVersionIdsAsync(DateTimeOffset.UtcNow, ct))
        {
            var version = await store.FindPackageVersionByIdAsync(id, ct);
            if (version?.Status != PackageVersionStatus.Approved) continue;
            version.QueueRescan();
            await store.AddAuditAsync(
                AuditEvent.Create("system", "ApprovalExpired", nameof(PackageVersion), id.ToString(),
                    "An expiring approval or waiver elapsed; rescan queued."), ct);
        }

        await store.SaveChangesAsync(ct);
    }
}

public sealed class CacheMaintenanceJob(IServiceScopeFactory scopes) : IBackgroundJob
{
    public string Name => "CacheMaintenance";
    public TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        var blobs = scope.ServiceProvider.GetRequiredService<IPackageBlobStore>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        foreach (var status in new[] { PackageVersionStatus.Blocked, PackageVersionStatus.Quarantined })
        foreach (var version in (await BackgroundJobPages.VersionsAsync(store, status, ct)).Where(x =>
                     x.UpdatedAt < cutoff))
        {
            if (!await blobs.DeleteUnapprovedAsync(version.Id, ct)) continue;
            await store.AddAuditAsync(
                AuditEvent.Create("system", "TransientArtifactDeleted", nameof(PackageVersion), version.Id.ToString(),
                    "Expired unapproved transient artifact deleted."), ct);
        }

        await store.SaveChangesAsync(ct);
    }
}

public sealed class LegacyBlobMigrationJob(IServiceScopeFactory scopes) : IBackgroundJob
{
    public string Name => "LegacyBlobMigration";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<ILegacyPackageBlobMigrator>();
        while (await migrator.MigrateBatchAsync(1, ct) > 0)
            await Task.Yield();
    }
}

public sealed class UpstreamHealthJob(IServiceScopeFactory scopes, IHttpClientFactory clients) : IBackgroundJob
{
    public string Name => "UpstreamHealth";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        var repositories = await BackgroundJobPages.RepositoriesAsync(store, ct);
        var client = clients.CreateClient("gateway-upstream");
        foreach (var repository in repositories.Where(x => x.Enabled))
        foreach (var snapshot in (await store.GetUpstreamsAsync(repository.Id, null, ct)).Where(x => x.Enabled))
        {
            var upstream = await store.FindUpstreamAsync(snapshot.Id, ct);
            if (upstream is null) continue;
            var previous = upstream.IsHealthy;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, upstream.Url);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var healthy = (int)response.StatusCode < 500;
                upstream.RecordHealth(healthy, healthy ? null : $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                upstream.RecordHealth(false, ex.Message);
            }

            if (previous != upstream.IsHealthy)
                await store.AddAuditAsync(
                    AuditEvent.Create("system", "UpstreamHealthChanged", nameof(Upstream), upstream.Id.ToString(),
                        upstream.IsHealthy == true ? "Upstream recovered." : "Upstream became unhealthy."), ct);
        }

        await store.SaveChangesAsync(ct);
    }
}

public sealed class OriginIntegrityMonitorJob(
    IServiceScopeFactory scopes,
    IPackageAcquisitionCoordinator coordinator,
    IOptions<GatewayInfrastructureOptions> options,
    ILogger<OriginIntegrityMonitorJob> logger) : IBackgroundJob
{
    public string Name => "OriginIntegrityMonitor";
    public TimeSpan Interval => options.Value.OriginIntegrityInterval;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        foreach (var version in await BackgroundJobPages.VersionsAsync(store, PackageVersionStatus.Approved, ct))
            try
            {
                await coordinator.VerifyOriginAsync(version.Id, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                logger.LogWarning(ex,
                    "Origin integrity verification could not complete for package version {PackageVersionId}",
                    version.Id);
            }
    }
}

internal static class BackgroundJobPages
{
    public static async Task<IReadOnlyList<PackageVersion>> VersionsAsync(IGatewayStore store,
        PackageVersionStatus status, CancellationToken ct)
    {
        var values = new List<PackageVersion>();
        var offset = 0;
        Page<PackageVersion> page;
        do
        {
            page = await store.GetPackageVersionsAsync(null, status, new PageRequest(offset, 100), ct);
            values.AddRange(page.Items);
            offset += page.Items.Count;
        } while (offset < page.TotalCount && page.Items.Count > 0);

        return values;
    }

    public static async Task<IReadOnlyList<Repository>> RepositoriesAsync(IGatewayStore store, CancellationToken ct)
    {
        var values = new List<Repository>();
        var offset = 0;
        Page<Repository> page;
        do
        {
            page = await store.GetRepositoriesAsync(new PageRequest(offset, 100), ct);
            values.AddRange(page.Items);
            offset += page.Items.Count;
        } while (offset < page.TotalCount && page.Items.Count > 0);

        return values;
    }
}