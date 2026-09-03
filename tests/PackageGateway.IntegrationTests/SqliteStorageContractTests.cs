using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PackageGateway.Application;
using PackageGateway.Domain;
using PackageGateway.Infrastructure;
using PackageGateway.Storage;
using Xunit;

namespace PackageGateway.IntegrationTests;

public sealed class SqliteStorageContractTests
{
    [Fact]
    public async Task Upstream_package_search_prefers_the_highest_priority_enabled_source()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = new GatewayStore(db);
        var repository = Repository.Create("NuGet", "nuget", PackageType.NuGet);
        await store.AddRepositoryAsync(repository, [], ct);
        var primary = Upstream.Create(repository.Id, "primary", new Uri("https://primary.example.test"), 0);
        var secondary = Upstream.Create(repository.Id, "secondary", new Uri("https://secondary.example.test"), 10);
        await store.AddUpstreamAsync(primary, ct);
        await store.AddUpstreamAsync(secondary, ct);
        await store.SaveChangesAsync(ct);
        var search = new UpstreamPackageSearch(store,
        [
            new FixtureSearchClient(new Dictionary<Guid, IReadOnlyList<UpstreamPackageDto>>
            {
                [primary.Id] =
                [
                    new UpstreamPackageDto(primary.Id, primary.Name, PackageType.NuGet, "Example", "2.0.0", "Primary",
                        null)
                ],
                [secondary.Id] =
                [
                    new UpstreamPackageDto(secondary.Id, secondary.Name, PackageType.NuGet, "Example", "1.0.0",
                        "Secondary", null),
                    new UpstreamPackageDto(secondary.Id, secondary.Name, PackageType.NuGet, "Other", "1.0.0", null,
                        null)
                ]
            })
        ]);

        var results = await search.SearchAsync(repository.Id, PackageType.NuGet, "example", 10, ct);
        var versions = await search.GetVersionsAsync(repository.Id, primary.Id, PackageType.NuGet, "Example", ct);

        Assert.Equal(2, results.Count);
        Assert.Equal(primary.Id, results.Single(x => x.Name == "Example").UpstreamId);
        Assert.Equal(["2.0.0", "1.0.0"], versions);
    }

    [Fact]
    public async Task Filesystem_blob_store_streams_content_outside_the_database()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"package-gateway-blobs-{Guid.CreateVersion7():N}");
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
            await using var db = new GatewayDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var store = new GatewayStore(db);
            var repository = Repository.Create("NuGet", "nuget", PackageType.NuGet);
            await store.AddRepositoryAsync(repository, [], ct);
            var upstream = Upstream.Create(repository.Id, "upstream", new Uri("https://example.test/v3/index.json"), 0);
            await store.AddUpstreamAsync(upstream, ct);
            await store.SaveChangesAsync(ct);
            var pair = await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.NuGet, "Example", "1.0.0",
                upstream.Id, "https://example.test/example.1.0.0.nupkg", null, null, null, ct);
            var bytes = new byte[] { 1, 2, 3, 4 };
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var blobs = new FileSystemPackageBlobStore(db,
                Options.Create(new BlobStorageOptions { Path = directory }));

            await blobs.StoreAsync(pair.Version.Id, new MemoryStream(bytes), sha256, 1024, ct);
            pair.Version.SetArtifact(sha256, bytes.Length);
            await store.SaveChangesAsync(ct);

            Assert.Empty(await db.PackageBlobs.ToListAsync(ct));
            var expectedPath = Path.Combine(directory, "sha256", sha256[..2], sha256[2..4], sha256);
            Assert.True(File.Exists(expectedPath));
            await using var content = await blobs.OpenReadAsync(pair.Version.Id, ct);
            Assert.NotNull(content);
            await using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Assert.Equal(bytes, copy.ToArray());
            pair.Version.BeginScan();
            pair.Version.CompleteScan(PackageVersionStatus.Approved, 0, false, SignatureStatus.Valid, false, "MIT");
            await store.SaveChangesAsync(ct);
            Assert.False(await blobs.DeleteUnapprovedAsync(pair.Version.Id, ct));
            pair.Version.ManuallyRequireReview();
            await store.SaveChangesAsync(ct);
            Assert.True(await blobs.DeleteUnapprovedAsync(pair.Version.Id, ct));
            Assert.False(File.Exists(expectedPath));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Legacy_database_blobs_are_migrated_to_the_filesystem()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"package-gateway-legacy-blobs-{Guid.CreateVersion7():N}");
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
            await using var db = new GatewayDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var store = new GatewayStore(db);
            var repository = Repository.Create("npm", "npm", PackageType.Npm);
            await store.AddRepositoryAsync(repository, [], ct);
            var upstream = Upstream.Create(repository.Id, "upstream", new Uri("https://registry.example.test"), 0,
                packageType: PackageType.Npm);
            await store.AddUpstreamAsync(upstream, ct);
            await store.SaveChangesAsync(ct);
            var pair = await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.Npm, "example", "1.0.0",
                upstream.Id, "https://registry.example.test/example.tgz", null, null, null, ct);
            var bytes = new byte[] { 5, 6, 7 };
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            db.PackageBlobs.Add(PackageBlob.Create(pair.Version.Id, bytes, sha256));
            pair.Version.SetArtifact(sha256, bytes.Length);
            await store.SaveChangesAsync(ct);
            var blobs = new FileSystemPackageBlobStore(db,
                Options.Create(new BlobStorageOptions { Path = directory }));

            Assert.Equal(1, await blobs.MigrateBatchAsync(2, ct));

            Assert.Empty(await db.PackageBlobs.ToListAsync(ct));
            await using var content = await blobs.OpenReadAsync(pair.Version.Id, ct);
            Assert.NotNull(content);
            await using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Assert.Equal(bytes, copy.ToArray());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Sqlite_migrations_apply_cleanly_and_are_current()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection,
            sqlite => sqlite.MigrationsAssembly("PackageGateway.Storage.Sqlite")).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync(ct);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
        Assert.True(!await db.VulnerabilityCacheEntries.AnyAsync(ct));
    }

    [Fact]
    public async Task Repository_slug_is_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        db.Repositories.Add(Repository.Create("One", "same", PackageType.NuGet));
        db.Repositories.Add(Repository.Create("Two", "same", PackageType.Npm));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Repository_can_store_same_normalized_name_for_both_formats()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = new GatewayStore(db);
        var repository = Repository.Create("Shared", "shared");
        await store.AddRepositoryAsync(repository, [], ct);
        var nuget = Upstream.Create(repository.Id, "NuGet", new Uri("https://api.nuget.org/v3/index.json"), 1,
            packageType: PackageType.NuGet);
        var npm = Upstream.Create(repository.Id, "npm", new Uri("https://registry.npmjs.org"), 1,
            packageType: PackageType.Npm);
        await store.AddUpstreamAsync(nuget, ct);
        await store.AddUpstreamAsync(npm, ct);
        await store.SaveChangesAsync(ct);
        await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.NuGet, "Example", "1.0.0", nuget.Id,
            "https://example.test/a", null, null, null, ct);
        await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.Npm, "example", "1.0.0", npm.Id,
            "https://example.test/b", null, null, null, ct);
        await store.SaveChangesAsync(ct);
        Assert.Equal(2, await db.Packages.CountAsync(ct));
        var nugetVersions = await store.GetPackageVersionsAsync(
            new PackageVersionListQuery(new PageRequest(), repository.Id, PackageType.NuGet, PackageName: "example"),
            ct);
        var npmVersions = await store.GetPackageVersionsAsync(
            new PackageVersionListQuery(new PageRequest(), repository.Id, PackageType.Npm, PackageName: "example"), ct);
        Assert.Single(nugetVersions.Items);
        Assert.Single(npmVersions.Items);
        Assert.NotEqual(nugetVersions.Items.Single().PackageId, npmVersions.Items.Single().PackageId);
    }

    [Fact]
    public async Task Approved_package_can_be_manually_queued_for_review_with_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = new GatewayStore(db);
        var repository = Repository.Create("NuGet", "nuget", PackageType.NuGet);
        await store.AddRepositoryAsync(repository, [], ct);
        var upstream = Upstream.Create(repository.Id, "upstream", new Uri("https://example.test/v3/index.json"), 0);
        await store.AddUpstreamAsync(upstream, ct);
        await store.SaveChangesAsync(ct);
        var pair = await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.NuGet, "Example", "1.0.0",
            upstream.Id, "https://example.test/example.1.0.0.nupkg", null, null, null, ct);
        pair.Version.SetArtifact(new string('a', 64), 42);
        pair.Version.BeginScan();
        pair.Version.CompleteScan(PackageVersionStatus.Approved, 0, false, SignatureStatus.Valid, false, "MIT");
        await store.SaveChangesAsync(ct);

        var result =
            await new GatewayManagementService(store, new TestPackageBlobStore()).RequireReviewAsync(pair.Version.Id,
                "Verify package ownership.",
                "reviewer", ct);

        Assert.Equal(PackageVersionStatus.ManualReview, result.Status);
        Assert.False(pair.Version.CanBeDelivered);
        var audit = await store.GetAuditEventsAsync(nameof(PackageVersion), pair.Version.Id.ToString(),
            new PageRequest(), ct);
        Assert.Contains(audit.Items, x => x.Action == "PackageReviewRequired" && x.Actor == "reviewer");
        var lowerCaseAudit = await store.GetAuditEventsAsync(nameof(PackageVersion).ToLowerInvariant(), null,
            new PageRequest(), ct);
        Assert.Contains(lowerCaseAudit.Items, x => x.Action == "PackageReviewRequired");
        var entityTypes = await store.GetAuditEventEntityTypesAsync(ct);
        Assert.Contains(nameof(PackageVersion), entityTypes);
    }

    [Fact]
    public async Task Administrator_can_remove_a_version_and_recreate_it_from_scratch()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = new GatewayStore(db);
        var repository = Repository.Create("NuGet", "nuget", PackageType.NuGet);
        await store.AddRepositoryAsync(repository, [], ct);
        var upstream = Upstream.Create(repository.Id, "upstream", new Uri("https://example.test/v3/index.json"), 0);
        await store.AddUpstreamAsync(upstream, ct);
        await store.SaveChangesAsync(ct);
        var pair = await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.NuGet, "Example", "1.0.0",
            upstream.Id, "https://example.test/example.1.0.0.nupkg", null, null, null, ct);
        var bytes = new byte[] { 1, 2, 3 };
        var blobs = new TestPackageBlobStore();
        await blobs.StoreAsync(pair.Version.Id, new MemoryStream(bytes), new string('a', 64), 1024, ct);
        pair.Version.SetArtifact(new string('a', 64), bytes.Length);
        pair.Version.BeginScan();
        pair.Version.CompleteScan(PackageVersionStatus.ManualReview, 40, false, SignatureStatus.Unsigned, false, "MIT");
        var scan = SecurityScan.Start(pair.Version.Id, "fixture");
        scan.Complete(ScanResult.Succeeded, 40);
        var rule = PolicyRuleResult.Create(pair.Version.Id, null, "CooldownPolicy", PolicyAction.ManualReview,
            "Publication time is unknown.");
        var finding = SecurityFinding.Create(scan.Id, "Vulnerability", FindingSeverity.Medium, "GHSA-fixture",
            "Fixture finding.", "fixture", riskScore: 30);
        await store.AddScanAsync(scan, [finding], [rule], ct);
        var approval = PackageApproval.Create(pair.Version.Id, ApprovalDecision.Reject, "Review it.", "reviewer");
        await store.AddApprovalAsync(approval, ct);
        await store.AddApprovalRuleResultsAsync(approval.Id, [rule.Id], ct);
        await store.StoreAsync("OSV.dev", PackageType.NuGet, "example", "1.0.0", "{\"vulns\":[]}",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24), ct);
        await store.SaveChangesAsync(ct);
        var originalPackageId = pair.Package.Id;
        var originalVersionId = pair.Version.Id;

        Assert.True(await new GatewayManagementService(store, blobs).RemovePackageVersionAsync(originalVersionId,
            "Re-evaluate from scratch.", "administrator", ct));

        Assert.Null(await store.FindPackageVersionByIdAsync(originalVersionId, ct));
        Assert.Equal(0, await db.PackageBlobs.CountAsync(ct));
        Assert.Equal(0, await db.SecurityScans.CountAsync(ct));
        Assert.Equal(0, await db.SecurityFindings.CountAsync(ct));
        Assert.Equal(0, await db.PolicyRuleResults.CountAsync(ct));
        Assert.Equal(0, await db.PackageApprovals.CountAsync(ct));
        Assert.Equal(0, await db.PackageApprovalRuleResults.CountAsync(ct));
        Assert.Equal(0, await db.VulnerabilityCacheEntries.CountAsync(ct));
        var audit = await store.GetAuditEventsAsync(nameof(PackageVersion), originalVersionId.ToString(),
            new PageRequest(), ct);
        Assert.Contains(audit.Items, x => x.Action == "PackageVersionRemoved" && x.Actor == "administrator");
        var recreated = await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.NuGet, "Example", "1.0.0",
            upstream.Id, "https://example.test/example.1.0.0.nupkg", null, null, null, ct);
        Assert.NotEqual(originalPackageId, recreated.Package.Id);
        Assert.NotEqual(originalVersionId, recreated.Version.Id);
        Assert.Equal(PackageVersionStatus.Pending, recreated.Version.Status);
    }

    [Fact]
    public async Task Vulnerability_cache_and_job_leases_are_durable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = new GatewayStore(db);
        var now = DateTimeOffset.UtcNow;
        await store.StoreAsync("OSV.dev", PackageType.Npm, "example", "1.0.0", "{\"vulns\":[]}", now, now.AddHours(24),
            ct);
        db.ChangeTracker.Clear();
        var cached = await store.FindAsync("OSV.dev", PackageType.Npm, "example", "1.0.0", ct);
        Assert.NotNull(cached);
        Assert.True(cached.ExpiresAt > now);
        Assert.True(await store.TryAcquireJobLeaseAsync("rescan", "worker-a", now, TimeSpan.FromMinutes(5), ct));
        db.ChangeTracker.Clear();
        Assert.False(await store.TryAcquireJobLeaseAsync("rescan", "worker-b", now.AddMinutes(1),
            TimeSpan.FromMinutes(5), ct));
        db.ChangeTracker.Clear();
        await store.CompleteJobLeaseAsync("rescan", "worker-a", now.AddMinutes(2), null, ct);
        db.ChangeTracker.Clear();
        Assert.True(await store.TryAcquireJobLeaseAsync("rescan", "worker-b", now.AddMinutes(3),
            TimeSpan.FromMinutes(5), ct));
    }

    [Fact]
    public async Task Waivers_are_expiring_and_scoped_to_non_hard_rule_results()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = new GatewayStore(db);
        var repository = Repository.Create("npm", "npm", PackageType.Npm);
        await store.AddRepositoryAsync(repository, [], ct);
        var upstream = Upstream.Create(repository.Id, "fixture", new Uri("https://example.test"), 0, true,
            PackageType.Npm);
        await store.AddUpstreamAsync(upstream, ct);
        await store.SaveChangesAsync(ct);
        var pair = await store.GetOrCreatePackageVersionAsync(repository.Id, PackageType.Npm, "example", "1.0.0",
            upstream.Id, "https://example.test/example.tgz", DateTimeOffset.UtcNow.AddDays(-10), null, null, ct);
        pair.Version.BeginScan();
        pair.Version.CompleteScan(PackageVersionStatus.ManualReview, 40, false, SignatureStatus.Unknown, true, "MIT");
        var scan = SecurityScan.Start(pair.Version.Id, "fixture");
        var waivable = PolicyRuleResult.Create(pair.Version.Id, null, "InstallScripts", PolicyAction.ManualReview,
            "Review script.");
        var hard = PolicyRuleResult.Create(pair.Version.Id, null, "Integrity", PolicyAction.Block, "Digest mismatch.",
            true);
        scan.Complete(ScanResult.Succeeded, 40);
        await store.AddScanAsync(scan, [], [waivable, hard], ct);
        await store.SaveChangesAsync(ct);
        var management = new GatewayManagementService(store, new TestPackageBlobStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() => management.DecideAsync(pair.Version.Id,
            ApprovalDecision.WaivePolicy, "unsafe", "reviewer", DateTimeOffset.UtcNow.AddHours(1), [hard.Id], ct));
        var result = await management.DecideAsync(pair.Version.Id, ApprovalDecision.WaivePolicy,
            "Reviewed lifecycle script.", "reviewer", DateTimeOffset.UtcNow.AddHours(1), [waivable.Id], ct);
        Assert.Equal(PackageVersionStatus.Approved, result.Status);
        var approvals = await store.GetApprovalsAsync(pair.Version.Id, new PageRequest(), ct);
        var links = await store.GetApprovalRuleResultIdsAsync(approvals.Items.Select(x => x.Id).ToArray(), ct);
        Assert.Contains(waivable.Id, links[approvals.Items.Single().Id]);
    }

    [Fact]
    public async Task Access_token_secret_is_one_time_and_repository_scope_isolated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var store = new GatewayStore(db);
        var first = Repository.Create("first", "first", PackageType.NuGet);
        var second = Repository.Create("second", "second", PackageType.NuGet);
        await store.AddRepositoryAsync(first, [], ct);
        await store.AddRepositoryAsync(second, [], ct);
        await store.SaveChangesAsync(ct);
        var tokens = new AccessTokenService(store,
            Options.Create(
            new GatewayInfrastructureOptions { TokenPepper = new string('x', 32) }));
        var created = await tokens.CreateAsync("restore", "admin", [$"repository:{first.Id}:read"],
            DateTimeOffset.UtcNow.AddDays(1), ct);
        Assert.True(await tokens.ValidateAsync(created.Secret, first.Id, ct));
        Assert.False(await tokens.ValidateAsync(created.Secret, second.Id, ct));
        db.ChangeTracker.Clear();
        var metadata = await store.GetAccessTokensAsync(new PageRequest(), ct);
        Assert.Single(metadata.Items);
        Assert.DoesNotContain(created.Secret, metadata.Items.Single().Verifier, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlServer_supports_the_common_repository_contract()
    {
        var ct = TestContext.Current.CancellationToken;
        var configured = Environment.GetEnvironmentVariable("PACKAGE_GATEWAY_SQLSERVER_TEST");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new SqlConnectionStringBuilder(configured)
            { InitialCatalog = $"PackageGatewayTests_{Guid.NewGuid():N}" };
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlServer(builder.ConnectionString,
            sql => sql.MigrationsAssembly("PackageGateway.Storage.SqlServer")).Options;
        await using var db = new GatewayDbContext(options);
        try
        {
            await db.Database.MigrateAsync(ct);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
            var store = new GatewayStore(db);
            var repository = Repository.Create("npm", "npm", PackageType.Npm);
            await store.AddRepositoryAsync(repository, [], ct);
            await store.SaveChangesAsync(ct);
            Assert.Equal(repository.Id, (await store.FindRepositoryBySlugAsync("npm", PackageType.Npm, ct))?.Id);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(ct);
        }
    }

    private sealed class FixtureSearchClient(
        IReadOnlyDictionary<Guid, IReadOnlyList<UpstreamPackageDto>> results) : IUpstreamClient
    {
        public PackageType PackageType => PackageType.NuGet;

        public Task<ResolvedArtifact?> ResolveExactAsync(Upstream upstream, string packageName, string version,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ResolvedArtifact?>(null);
        }

        public Task<IReadOnlyList<UpstreamPackageDto>> SearchAsync(Upstream upstream, string query, int take,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(results.GetValueOrDefault(upstream.Id, []));
        }

        public Task<IReadOnlyList<string>> GetVersionsAsync(Upstream upstream, string packageName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>(["2.0.0", "1.0.0"]);
        }
    }
}
