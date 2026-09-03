using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PackageGateway.Application;
using PackageGateway.Domain;

namespace PackageGateway.Storage;

public sealed class GatewayStore(GatewayDbContext db) : IGatewayStore, IVulnerabilityCacheStore
{
    public Task<Repository?> FindRepositoryBySlugAsync(string slug, PackageType? packageType, CancellationToken ct)
    {
        return db.Repositories.SingleOrDefaultAsync(x => !x.IsDeleted && x.Enabled && x.Slug == slug &&
                                                         (packageType == null || x.PackageType == packageType ||
                                                          db.Upstreams.Any(u =>
                                                              u.RepositoryId == x.Id && !u.IsDeleted && u.Enabled &&
                                                              u.PackageType == packageType)), ct);
    }

    public Task<Repository?> FindRepositoryAsync(Guid id, CancellationToken ct)
    {
        return db.Repositories.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    }

    public async Task<Page<Repository>> GetRepositoriesAsync(PageRequest page, CancellationToken ct)
    {
        var query = db.Repositories.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name);
        return new Page<Repository>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<Page<Repository>> GetRepositoriesAsync(RepositoryListQuery request, CancellationToken ct)
    {
        var query = db.Repositories.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Name.ToLower().Contains(search) || x.Slug.Contains(search));
        }

        if (request.PackageType is { } type)
            query = query.Where(x =>
                x.PackageType == type ||
                db.Upstreams.Any(u => u.RepositoryId == x.Id && !u.IsDeleted && u.PackageType == type));
        if (request.Enabled is { } enabled) query = query.Where(x => x.Enabled == enabled);
        query = (request.SortBy, request.Direction) switch
        {
            (RepositorySortField.CreatedAt, SortDirection.Ascending) => query.OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id),
            (RepositorySortField.CreatedAt, SortDirection.Descending) => query.OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.Id),
            (RepositorySortField.UpdatedAt, SortDirection.Ascending) => query.OrderBy(x => x.UpdatedAt)
                .ThenBy(x => x.Id),
            (RepositorySortField.UpdatedAt, SortDirection.Descending) => query.OrderByDescending(x => x.UpdatedAt)
                .ThenBy(x => x.Id),
            (_, SortDirection.Descending) => query.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.Name).ThenBy(x => x.Id)
        };
        return new Page<Repository>(
            await query.Skip(request.Page.SafeOffset).Take(request.Page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), request.Page.SafeOffset, request.Page.SafeLimit);
    }

    public async Task AddRepositoryAsync(Repository repository, IEnumerable<Policy> seedPolicies, CancellationToken ct)
    {
        db.Repositories.Add(repository);
        foreach (var policy in seedPolicies)
        {
            db.Policies.Add(policy);
            db.RepositoryPolicies.Add(RepositoryPolicy.Create(repository.Id, policy.Id));
        }

        await Task.CompletedTask;
    }

    public Task<Upstream?> FindUpstreamAsync(Guid id, CancellationToken ct)
    {
        return db.Upstreams.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    }

    public Task<IReadOnlyList<Upstream>> GetUpstreamsAsync(Guid repositoryId, PackageType? packageType,
        CancellationToken ct)
    {
        return GetUpstreamsCore(repositoryId, packageType, ct);
    }

    public async Task AddUpstreamAsync(Upstream upstream, CancellationToken ct)
    {
        db.Upstreams.Add(upstream);
        await Task.CompletedTask;
    }

    public async Task<Page<Policy>> GetPoliciesAsync(Guid? repositoryId, PageRequest page, CancellationToken ct)
    {
        var query = db.Policies.AsNoTracking().Where(x => !x.IsDeleted);
        if (repositoryId is { } id)
            query = from policy in query
                join assignment in db.RepositoryPolicies on policy.Id equals assignment.PolicyId
                where assignment.RepositoryId == id
                select policy;
        query = query.OrderBy(x => x.Name);
        return new Page<Policy>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public Task<Policy?> FindPolicyAsync(Guid id, CancellationToken ct)
    {
        return db.Policies.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    }

    public async Task AddPolicyAsync(Policy policy, CancellationToken ct)
    {
        db.Policies.Add(policy);
        await Task.CompletedTask;
    }

    public async Task AssignPolicyAsync(Guid repositoryId, Guid policyId, CancellationToken ct)
    {
        if (!await db.RepositoryPolicies.AnyAsync(x => x.RepositoryId == repositoryId && x.PolicyId == policyId, ct))
            db.RepositoryPolicies.Add(RepositoryPolicy.Create(repositoryId, policyId));
    }

    public async Task UnassignPolicyAsync(Guid repositoryId, Guid policyId, CancellationToken ct)
    {
        var item = await db.RepositoryPolicies.FindAsync([repositoryId, policyId], ct);
        if (item is not null) db.RepositoryPolicies.Remove(item);
    }

    public async Task<IReadOnlyList<Policy>> GetAssignedPoliciesAsync(Guid repositoryId, CancellationToken ct)
    {
        return await (from policy in db.Policies.AsNoTracking()
            join assignment in db.RepositoryPolicies on policy.Id equals assignment.PolicyId
            where assignment.RepositoryId == repositoryId && policy.Enabled && !policy.IsDeleted
            select policy).ToListAsync(ct);
    }

    public async Task<(Package Package, PackageVersion Version)?> FindPackageVersionAsync(Guid repositoryId,
        PackageType packageType, string normalizedName, string version, CancellationToken ct)
    {
        var result = await (from package in db.Packages.AsNoTracking()
            join packageVersion in db.PackageVersions.AsNoTracking() on package.Id equals packageVersion.PackageId
            where package.RepositoryId == repositoryId && package.PackageType == packageType &&
                  package.NormalizedName == normalizedName && packageVersion.Version == version
            select new { package, packageVersion }).SingleOrDefaultAsync(ct);
        return result is null ? null : (result.package, result.packageVersion);
    }

    public Task<Package?> FindPackageAsync(Guid id, CancellationToken ct)
    {
        return db.Packages.SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(Package Package, PackageVersion Version)> GetOrCreatePackageVersionAsync(Guid repositoryId,
        PackageType type, string name, string version, Guid upstreamId, string artifactUrl, DateTimeOffset? publishedAt,
        string? expectedSha256, string? expectedIntegrity, CancellationToken ct)
    {
        var normalized = PackageIdentity.Normalize(name, type);
        var existing = await FindTrackedPackageVersionAsync(repositoryId, type, normalized, version, ct);
        if (existing is not null) return existing.Value;
        var package = await db.Packages.SingleOrDefaultAsync(
            x => x.RepositoryId == repositoryId && x.PackageType == type && x.NormalizedName == normalized, ct);
        if (package is null)
        {
            package = Package.Create(repositoryId, name, type);
            db.Packages.Add(package);
        }

        var packageVersion = PackageVersion.Create(package.Id, version, upstreamId, artifactUrl, publishedAt,
            expectedSha256, expectedIntegrity);
        db.PackageVersions.Add(packageVersion);
        return (package, packageVersion);
    }

    public Task<PackageVersion?> FindPackageVersionByIdAsync(Guid id, CancellationToken ct)
    {
        return db.PackageVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<bool> RemovePackageVersionAsync(Guid id, CancellationToken ct)
    {
        var version = await db.PackageVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (version is null) return false;
        var package = await db.Packages.SingleAsync(x => x.Id == version.PackageId, ct);
        var scanIds = await db.SecurityScans.Where(x => x.PackageVersionId == id).Select(x => x.Id).ToArrayAsync(ct);
        var approvalIds = await db.PackageApprovals.Where(x => x.PackageVersionId == id).Select(x => x.Id)
            .ToArrayAsync(ct);
        var ruleIds = await db.PolicyRuleResults.Where(x => x.PackageVersionId == id).Select(x => x.Id)
            .ToArrayAsync(ct);
        db.PackageApprovalRuleResults.RemoveRange(await db.PackageApprovalRuleResults
            .Where(x => approvalIds.Contains(x.PackageApprovalId) || ruleIds.Contains(x.PolicyRuleResultId))
            .ToListAsync(ct));
        db.SecurityFindings.RemoveRange(await db.SecurityFindings.Where(x => scanIds.Contains(x.SecurityScanId))
            .ToListAsync(ct));
        db.PackageApprovals.RemoveRange(await db.PackageApprovals.Where(x => x.PackageVersionId == id).ToListAsync(ct));
        db.PolicyRuleResults.RemoveRange(
            await db.PolicyRuleResults.Where(x => x.PackageVersionId == id).ToListAsync(ct));
        db.SecurityScans.RemoveRange(await db.SecurityScans.Where(x => x.PackageVersionId == id).ToListAsync(ct));
        var blob = await db.PackageBlobs.SingleOrDefaultAsync(x => x.PackageVersionId == id, ct);
        if (blob is not null) db.PackageBlobs.Remove(blob);
        db.VulnerabilityCacheEntries.RemoveRange(await db.VulnerabilityCacheEntries.Where(x =>
            x.PackageType == package.PackageType && x.NormalizedName == package.NormalizedName &&
            x.Version == version.Version).ToListAsync(ct));
        db.PackageVersions.Remove(version);
        if (!await db.PackageVersions.AnyAsync(x => x.PackageId == package.Id && x.Id != id, ct))
            db.Packages.Remove(package);
        return true;
    }

    public async Task<Page<Package>> GetPackagesAsync(Guid repositoryId, string? search, PageRequest page,
        CancellationToken ct)
    {
        var query = db.Packages.AsNoTracking().Where(x => x.RepositoryId == repositoryId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.NormalizedName.Contains(value));
        }

        query = query.OrderBy(x => x.NormalizedName);
        return new Page<Package>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<Page<Package>> GetPackagesAsync(PackageListQuery request, CancellationToken ct)
    {
        var query = db.Packages.AsNoTracking().Where(x => x.RepositoryId == request.RepositoryId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.NormalizedName.Contains(search));
        }

        query = (request.SortBy, request.Direction) switch
        {
            (PackageSortField.CreatedAt, SortDirection.Ascending) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            (PackageSortField.CreatedAt, SortDirection.Descending) => query.OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.Id),
            (_, SortDirection.Descending) => query.OrderByDescending(x => x.NormalizedName).ThenBy(x => x.Id),
            _ => query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id)
        };
        return new Page<Package>(await query.Skip(request.Page.SafeOffset).Take(request.Page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), request.Page.SafeOffset, request.Page.SafeLimit);
    }

    public async Task<Page<PackageVersion>> GetPackageVersionsAsync(Guid? repositoryId, PackageVersionStatus? status,
        PageRequest page, CancellationToken ct)
    {
        var query = db.PackageVersions.AsNoTracking().AsQueryable();
        if (status is { } s) query = query.Where(x => x.Status == s);
        if (repositoryId is { } id)
            query = from version in query
                join package in db.Packages on version.PackageId equals package.Id
                where package.RepositoryId == id
                select version;
        query = query.OrderByDescending(x => x.FirstSeenAt);
        return new Page<PackageVersion>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<Page<PackageVersion>> GetPackageVersionsAsync(PackageVersionListQuery request,
        CancellationToken ct)
    {
        var query = db.PackageVersions.AsNoTracking();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.RepositoryId is not null || request.PackageType is not null ||
            !string.IsNullOrWhiteSpace(request.PackageName))
        {
            var normalizedName = request.PackageName?.Trim().ToLowerInvariant();
            query = from item in query
                join package in db.Packages on item.PackageId equals package.Id
                where (request.RepositoryId == null || package.RepositoryId == request.RepositoryId.Value) &&
                      (request.PackageType == null || package.PackageType == request.PackageType.Value) &&
                      (normalizedName == null || package.NormalizedName.Contains(normalizedName))
                select item;
        }

        query = (request.SortBy, request.Direction) switch
        {
            (PackageVersionSortField.Version, SortDirection.Ascending) => query.OrderBy(x => x.Version)
                .ThenBy(x => x.Id),
            (PackageVersionSortField.Version, SortDirection.Descending) => query.OrderByDescending(x => x.Version)
                .ThenBy(x => x.Id),
            (PackageVersionSortField.LastScannedAt, SortDirection.Ascending) => query.OrderBy(x => x.LastScannedAt)
                .ThenBy(x => x.Id),
            (PackageVersionSortField.LastScannedAt, SortDirection.Descending) => query
                .OrderByDescending(x => x.LastScannedAt).ThenBy(x => x.Id),
            (PackageVersionSortField.RiskScore, SortDirection.Ascending) => query.OrderBy(x => x.RiskScore)
                .ThenBy(x => x.Id),
            (PackageVersionSortField.RiskScore, SortDirection.Descending) => query.OrderByDescending(x => x.RiskScore)
                .ThenBy(x => x.Id),
            (PackageVersionSortField.Status, SortDirection.Ascending) => query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            (PackageVersionSortField.Status, SortDirection.Descending) => query.OrderByDescending(x => x.Status)
                .ThenBy(x => x.Id),
            (_, SortDirection.Ascending) => query.OrderBy(x => x.FirstSeenAt).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.FirstSeenAt).ThenBy(x => x.Id)
        };
        return new Page<PackageVersion>(
            await query.Skip(request.Page.SafeOffset).Take(request.Page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), request.Page.SafeOffset, request.Page.SafeLimit);
    }

    public async Task AddScanAsync(SecurityScan scan, IReadOnlyList<SecurityFinding> findings,
        IReadOnlyList<PolicyRuleResult> rules, CancellationToken ct)
    {
        db.SecurityScans.Add(scan);
        db.SecurityFindings.AddRange(findings);
        db.PolicyRuleResults.AddRange(rules);
        await Task.CompletedTask;
    }

    public async Task<Page<SecurityScan>> GetScansAsync(Guid packageVersionId, PageRequest page, CancellationToken ct)
    {
        var query = db.SecurityScans.AsNoTracking().Where(x => x.PackageVersionId == packageVersionId)
            .OrderByDescending(x => x.StartedAt);
        return new Page<SecurityScan>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<Page<PolicyRuleResult>> GetRuleResultsAsync(Guid packageVersionId, PageRequest page,
        CancellationToken ct)
    {
        var query = db.PolicyRuleResults.AsNoTracking().Where(x => x.PackageVersionId == packageVersionId)
            .OrderByDescending(x => x.EvaluatedAt);
        return new Page<PolicyRuleResult>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<IReadOnlyList<PolicyRuleResult>> GetRuleResultsByIdsAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        return await db.PolicyRuleResults.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
    }

    public async Task<Page<SecurityFinding>> GetFindingsAsync(Guid? packageVersionId, FindingSeverity? minimumSeverity,
        PageRequest page, CancellationToken ct)
    {
        var query = db.SecurityFindings.AsNoTracking().AsQueryable();
        if (packageVersionId is { } id)
            query = from finding in query
                join scan in db.SecurityScans on finding.SecurityScanId equals scan.Id
                where scan.PackageVersionId == id
                select finding;
        if (minimumSeverity is { } severity) query = query.Where(x => x.Severity >= severity);
        query = query.OrderByDescending(x => x.CreatedAt);
        return new Page<SecurityFinding>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task AddApprovalAsync(PackageApproval approval, CancellationToken ct)
    {
        db.PackageApprovals.Add(approval);
        await Task.CompletedTask;
    }

    public async Task AddApprovalRuleResultsAsync(Guid approvalId, IReadOnlyCollection<Guid> ruleResultIds,
        CancellationToken ct)
    {
        db.PackageApprovalRuleResults.AddRange(ruleResultIds.Distinct()
            .Select(id => PackageApprovalRuleResult.Create(approvalId, id)));
        await Task.CompletedTask;
    }

    public async Task<Page<PackageApproval>> GetApprovalsAsync(Guid packageVersionId, PageRequest page,
        CancellationToken ct)
    {
        var query = db.PackageApprovals.AsNoTracking().Where(x => x.PackageVersionId == packageVersionId)
            .OrderByDescending(x => x.CreatedAt);
        return new Page<PackageApproval>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetApprovalRuleResultIdsAsync(
        IReadOnlyCollection<Guid> approvalIds, CancellationToken ct)
    {
        return (await db.PackageApprovalRuleResults.AsNoTracking().Where(x => approvalIds.Contains(x.PackageApprovalId))
                .ToListAsync(ct))
            .GroupBy(x => x.PackageApprovalId).ToDictionary(x => x.Key,
                x => (IReadOnlyList<Guid>)x.Select(y => y.PolicyRuleResultId).ToArray());
    }

    public async Task AddAuditAsync(AuditEvent auditEvent, CancellationToken ct)
    {
        db.AuditEvents.Add(auditEvent);
        await Task.CompletedTask;
    }

    public async Task<Page<AuditEvent>> GetAuditEventsAsync(string? entityType, string? entityId, PageRequest page,
        CancellationToken ct)
    {
        var query = db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var normalizedEntityType = entityType.Trim().ToUpperInvariant();
            query = query.Where(x => x.EntityType.ToUpper() == normalizedEntityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(x => x.EntityId == entityId);
        query = query.OrderByDescending(x => x.Timestamp);
        return new Page<AuditEvent>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<IReadOnlyList<string>> GetAuditEventEntityTypesAsync(CancellationToken ct)
    {
        return await db.AuditEvents.AsNoTracking().Select(x => x.EntityType).Distinct().OrderBy(x => x).ToListAsync(ct);
    }

    public async Task AddAccessTokenAsync(AccessToken token, CancellationToken ct)
    {
        db.AccessTokens.Add(token);
        await Task.CompletedTask;
    }

    public Task<AccessToken?> FindAccessTokenAsync(Guid id, CancellationToken ct)
    {
        return db.AccessTokens.SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<AccessToken?> FindAccessTokenByTokenIdAsync(string tokenId, CancellationToken ct)
    {
        return db.AccessTokens.SingleOrDefaultAsync(x => x.TokenId == tokenId, ct);
    }

    public async Task<Page<AccessToken>> GetAccessTokensAsync(PageRequest page, CancellationToken ct)
    {
        var query = db.AccessTokens.AsNoTracking().OrderByDescending(x => x.CreatedAt);
        return new Page<AccessToken>(await query.Skip(page.SafeOffset).Take(page.SafeLimit).ToListAsync(ct),
            await query.CountAsync(ct), page.SafeOffset, page.SafeLimit);
    }

    public async Task<IReadOnlyList<Guid>> GetExpiredApprovalVersionIdsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var approvals = await db.PackageApprovals
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now && x.ProcessedAt == null).ToListAsync(ct);
        foreach (var approval in approvals) approval.MarkProcessed();
        return approvals.Select(x => x.PackageVersionId).Distinct().ToArray();
    }

    public async Task<bool> TryAcquireJobLeaseAsync(string jobName, string owner, DateTimeOffset now, TimeSpan duration,
        CancellationToken ct)
    {
        var state = await db.BackgroundJobStates.SingleOrDefaultAsync(x => x.Name == jobName, ct);
        if (state is null)
        {
            state = BackgroundJobState.Create(jobName);
            db.BackgroundJobStates.Add(state);
        }

        if (!state.TryAcquire(owner, now, duration)) return false;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(state).State = EntityState.Detached;
            return false;
        }
    }

    public async Task CompleteJobLeaseAsync(string jobName, string owner, DateTimeOffset now, string? error,
        CancellationToken ct)
    {
        var state = await db.BackgroundJobStates.SingleAsync(x => x.Name == jobName, ct);
        state.Complete(owner, now, error);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = GatewayDiagnostics.Activities.StartActivity("database.save");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            GatewayDiagnostics.DatabaseSaveDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("db.system", db.Database.ProviderName));
        }
    }

    public Task<bool> CanConnectAsync(CancellationToken ct)
    {
        return db.Database.CanConnectAsync(ct);
    }

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken ct)
    {
        return (await db.Database.GetPendingMigrationsAsync(ct)).Any();
    }

    public Task MigrateAsync(CancellationToken ct)
    {
        return db.Database.MigrateAsync(ct);
    }

    public Task<VulnerabilityCacheEntry?> FindAsync(string provider, PackageType packageType, string normalizedName,
        string version, CancellationToken ct)
    {
        return db.VulnerabilityCacheEntries.AsNoTracking().SingleOrDefaultAsync(
            x => x.Provider == provider && x.PackageType == packageType && x.NormalizedName == normalizedName &&
                 x.Version == version, ct);
    }

    public async Task StoreAsync(string provider, PackageType packageType, string normalizedName, string version,
        string payloadJson, DateTimeOffset fetchedAt, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var entry = await db.VulnerabilityCacheEntries.SingleOrDefaultAsync(
            x => x.Provider == provider && x.PackageType == packageType && x.NormalizedName == normalizedName &&
                 x.Version == version, ct);
        if (entry is null)
            db.VulnerabilityCacheEntries.Add(VulnerabilityCacheEntry.Create(provider, packageType, normalizedName,
                version, payloadJson, fetchedAt, expiresAt));
        else entry.Refresh(payloadJson, fetchedAt, expiresAt);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<Upstream>> GetUpstreamsCore(Guid repositoryId, PackageType? packageType,
        CancellationToken ct)
    {
        return await db.Upstreams.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId && !x.IsDeleted &&
                        (packageType == null || x.PackageType == packageType)).OrderBy(x => x.Priority)
            .ThenBy(x => x.Id).ToListAsync(ct);
    }

    private async Task<(Package Package, PackageVersion Version)?> FindTrackedPackageVersionAsync(Guid repositoryId,
        PackageType packageType, string normalizedName, string version, CancellationToken ct)
    {
        var result = await (from package in db.Packages
            join packageVersion in db.PackageVersions on package.Id equals packageVersion.PackageId
            where package.RepositoryId == repositoryId && package.PackageType == packageType &&
                  package.NormalizedName == normalizedName && packageVersion.Version == version
            select new { package, packageVersion }).SingleOrDefaultAsync(ct);
        return result is null ? null : (result.package, result.packageVersion);
    }
}