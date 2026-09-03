using System.Globalization;
using System.Text;
using PackageGateway.Domain;

namespace PackageGateway.Application;

public sealed record PageRequest(int Offset = 0, int Limit = 25)
{
    public int SafeOffset => Math.Max(0, Offset);
    public int SafeLimit => Math.Clamp(Limit, 1, 100);
}

public sealed record Page<T>(IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit);

public enum SortDirection
{
    Ascending,
    Descending
}

public enum RepositorySortField
{
    Name,
    CreatedAt,
    UpdatedAt
}

public enum PackageSortField
{
    Name,
    CreatedAt
}

public enum PackageVersionSortField
{
    Version,
    FirstSeenAt,
    LastScannedAt,
    RiskScore,
    Status
}

public sealed record RepositoryListQuery(
    PageRequest Page,
    string? Search = null,
    PackageType? PackageType = null,
    bool? Enabled = null,
    RepositorySortField SortBy = RepositorySortField.Name,
    SortDirection Direction = SortDirection.Ascending);

public sealed record PackageListQuery(
    Guid RepositoryId,
    PageRequest Page,
    string? Search = null,
    PackageSortField SortBy = PackageSortField.Name,
    SortDirection Direction = SortDirection.Ascending);

public sealed record PackageVersionListQuery(
    PageRequest Page,
    Guid? RepositoryId = null,
    PackageType? PackageType = null,
    PackageVersionStatus? Status = null,
    string? PackageName = null,
    PackageVersionSortField SortBy = PackageVersionSortField.FirstSeenAt,
    SortDirection Direction = SortDirection.Descending);

public sealed record ConnectionEdge<T>(string Cursor, T Node);

public sealed record ConnectionPageInfo(bool HasNextPage, bool HasPreviousPage, string? StartCursor, string? EndCursor);

public sealed record Connection<T>(
    IReadOnlyList<ConnectionEdge<T>> Edges,
    IReadOnlyList<T> Nodes,
    ConnectionPageInfo PageInfo,
    int TotalCount);

public static class CursorPaging
{
    public static PageRequest Request(string? after, int? first)
    {
        var offset = 0;
        if (!string.IsNullOrWhiteSpace(after))
            try
            {
                offset = int.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(after)),
                    CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new ArgumentException("Invalid paging cursor.", nameof(after));
            }

        return new PageRequest(offset, first ?? 25);
    }

    public static Connection<T> ToConnection<T>(Page<T> page)
    {
        var edges = page.Items.Select((item, index) => new ConnectionEdge<T>(Cursor(page.Offset + index + 1), item))
            .ToArray();
        return new Connection<T>(edges, page.Items,
            new ConnectionPageInfo(page.Offset + page.Items.Count < page.TotalCount, page.Offset > 0,
                edges.FirstOrDefault()?.Cursor, edges.LastOrDefault()?.Cursor), page.TotalCount);
    }

    private static string Cursor(int offset)
    {
        return Convert.ToBase64String(
            Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));
    }
}

public sealed record RepositoryDto(
    Guid Id,
    string Name,
    string Slug,
    PackageType? PackageType,
    bool Enabled,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpstreamDto(
    Guid Id,
    Guid RepositoryId,
    string Name,
    string Url,
    PackageType PackageType,
    int Priority,
    bool Enabled,
    bool Trusted,
    bool? IsHealthy,
    DateTimeOffset? LastHealthCheckAt,
    string? HealthDetail);

public sealed record PolicyDto(
    Guid Id,
    string Name,
    string Type,
    int SchemaVersion,
    string ConfigJson,
    IReadOnlySet<PackageType> PackageTypes,
    bool Enabled);

public sealed record PackageDto(
    Guid Id,
    Guid RepositoryId,
    string Name,
    string NormalizedName,
    PackageType PackageType);

public sealed record PackageVersionDto(
    Guid Id,
    Guid PackageId,
    string Version,
    PackageVersionStatus Status,
    string? Sha256,
    long? Size,
    int RiskScore,
    bool HasHardBlock,
    SignatureStatus SignatureStatus,
    bool HasInstallScripts,
    string? License,
    string DecisionExplanation,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastScannedAt);

public sealed record UpstreamPackageDto(
    Guid UpstreamId,
    string UpstreamName,
    PackageType PackageType,
    string Name,
    string Version,
    string? Description,
    DateTimeOffset? PublishedAt);

public sealed record FindingDto(
    Guid Id,
    Guid ScanId,
    string Type,
    FindingSeverity Severity,
    string Title,
    string Description,
    string Source,
    string? ExternalReference,
    bool IsHardBlock,
    int RiskScore,
    DateTimeOffset CreatedAt);

public sealed record SecurityScanDto(
    Guid Id,
    Guid PackageVersionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string ScannerVersion,
    ScanResult? Result,
    int RiskScore);

public sealed record PolicyRuleResultDto(
    Guid Id,
    Guid PackageVersionId,
    Guid? PolicyId,
    string Rule,
    PolicyAction Action,
    string Reason,
    bool IsHardBlock,
    DateTimeOffset EvaluatedAt);

public sealed record PackageApprovalDto(
    Guid Id,
    Guid PackageVersionId,
    ApprovalDecision Decision,
    string Reason,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ProcessedAt,
    IReadOnlyList<Guid> AffectedRuleResultIds);

public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string EntityType,
    string EntityId,
    string Description,
    string DataJson);

public sealed record AccessTokenDto(
    Guid Id,
    string Name,
    string TokenId,
    string Owner,
    IReadOnlySet<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool Enabled);

public sealed record CreatedAccessToken(AccessTokenDto Token, string Secret);

public sealed record ComponentStatusDto(string Name, bool Healthy, string? Detail);

public sealed record SystemStatusDto(
    string Version,
    DateTimeOffset StartedAt,
    ComponentStatusDto Database,
    ComponentStatusDto BackgroundScanner,
    IReadOnlyList<ComponentStatusDto> VulnerabilityProviders);

public sealed record CreateRepositoryCommand(string Name, string Slug, PackageType? PackageType, string? Description);

public sealed record UpdateRepositoryCommand(Guid Id, string Name, string? Description, bool Enabled);

public sealed record CreateUpstreamCommand(
    Guid RepositoryId,
    string Name,
    string Url,
    PackageType PackageType,
    int Priority,
    bool Trusted);

public sealed record UpdateUpstreamCommand(
    Guid Id,
    string Name,
    string Url,
    PackageType PackageType,
    int Priority,
    bool Enabled,
    bool Trusted);

public sealed record CreatePolicyCommand(
    string Name,
    string Type,
    int SchemaVersion,
    string ConfigJson,
    IReadOnlyList<PackageType>? PackageTypes = null);

public sealed record UpdatePolicyCommand(
    Guid Id,
    string Name,
    int SchemaVersion,
    string ConfigJson,
    bool Enabled,
    IReadOnlyList<PackageType>? PackageTypes = null);

public sealed record ArtifactRequest(
    Guid RepositoryId,
    string RepositorySlug,
    PackageType PackageType,
    string PackageName,
    string Version,
    Guid UpstreamId,
    Uri ArtifactUri,
    DateTimeOffset? PublishedAt = null,
    string? ExpectedSha256 = null,
    string? ExpectedIntegrity = null);

public sealed record ResolvedArtifact(
    Guid UpstreamId,
    Uri ArtifactUri,
    DateTimeOffset? PublishedAt = null,
    string? ExpectedSha256 = null,
    string? ExpectedIntegrity = null);

public enum ArtifactDeliveryStatus
{
    Approved,
    Pending,
    Denied,
    NotFound,
    Failed
}

public sealed record ArtifactDelivery(
    ArtifactDeliveryStatus Status,
    Stream? Content = null,
    string? ContentType = null,
    long? Length = null,
    string? Sha256 = null,
    string? Message = null)
{
    public static ArtifactDelivery Pending(string message)
    {
        return new ArtifactDelivery(ArtifactDeliveryStatus.Pending, Message: message);
    }

    public static ArtifactDelivery Denied(string message)
    {
        return new ArtifactDelivery(ArtifactDeliveryStatus.Denied, Message: message);
    }

    public static ArtifactDelivery Failed(string message)
    {
        return new ArtifactDelivery(ArtifactDeliveryStatus.Failed, Message: message);
    }
}

public sealed record ScanFinding(
    string Type,
    FindingSeverity Severity,
    string Title,
    string Description,
    string Source,
    string? ExternalReference = null,
    bool IsHardBlock = false,
    int RiskScore = 0);

public sealed record PackageInspectionResult(
    IReadOnlyList<ScanFinding> Findings,
    int RiskScore,
    bool HasInstallScripts,
    SignatureStatus SignatureStatus,
    string? License,
    string? PackageName = null);

public sealed record Vulnerability(
    string ExternalId,
    FindingSeverity Severity,
    double? CvssScore,
    string Summary,
    string? Url);

public sealed record PolicyEvaluationContext(
    Package Package,
    PackageVersion Version,
    PackageInspectionResult Inspection,
    IReadOnlyList<Vulnerability> Vulnerabilities,
    IReadOnlyList<Policy> Policies,
    DateTimeOffset EvaluatedAt);

public sealed record RuleEvaluation(
    string Rule,
    PolicyAction Action,
    string Reason,
    Guid? PolicyId = null,
    bool IsHardBlock = false);

public sealed record PolicyEvaluation(
    PolicyAction FinalAction,
    int RiskScore,
    bool HasHardBlock,
    IReadOnlyList<RuleEvaluation> Rules);

public interface IGatewayStore
{
    Task<Repository?> FindRepositoryBySlugAsync(string slug, PackageType? packageType,
        CancellationToken cancellationToken);

    Task<Repository?> FindRepositoryAsync(Guid id, CancellationToken cancellationToken);
    Task<Page<Repository>> GetRepositoriesAsync(PageRequest page, CancellationToken cancellationToken);
    Task<Page<Repository>> GetRepositoriesAsync(RepositoryListQuery query, CancellationToken cancellationToken);

    Task AddRepositoryAsync(Repository repository, IEnumerable<Policy> seedPolicies,
        CancellationToken cancellationToken);

    Task<Upstream?> FindUpstreamAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Upstream>> GetUpstreamsAsync(Guid repositoryId, PackageType? packageType,
        CancellationToken cancellationToken);

    Task AddUpstreamAsync(Upstream upstream, CancellationToken cancellationToken);
    Task<Page<Policy>> GetPoliciesAsync(Guid? repositoryId, PageRequest page, CancellationToken cancellationToken);
    Task<Policy?> FindPolicyAsync(Guid id, CancellationToken cancellationToken);
    Task AddPolicyAsync(Policy policy, CancellationToken cancellationToken);
    Task AssignPolicyAsync(Guid repositoryId, Guid policyId, CancellationToken cancellationToken);
    Task UnassignPolicyAsync(Guid repositoryId, Guid policyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Policy>> GetAssignedPoliciesAsync(Guid repositoryId, CancellationToken cancellationToken);

    Task<(Package Package, PackageVersion Version)?> FindPackageVersionAsync(Guid repositoryId, PackageType packageType,
        string normalizedName, string version, CancellationToken cancellationToken);

    Task<Package?> FindPackageAsync(Guid id, CancellationToken cancellationToken);

    Task<(Package Package, PackageVersion Version)> GetOrCreatePackageVersionAsync(Guid repositoryId, PackageType type,
        string name, string version, Guid upstreamId, string artifactUrl, DateTimeOffset? publishedAt,
        string? expectedSha256, string? expectedIntegrity, CancellationToken cancellationToken);

    Task<PackageVersion?> FindPackageVersionByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> RemovePackageVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<Page<Package>> GetPackagesAsync(Guid repositoryId, string? search, PageRequest page,
        CancellationToken cancellationToken);

    Task<Page<Package>> GetPackagesAsync(PackageListQuery query, CancellationToken cancellationToken);

    Task<Page<PackageVersion>> GetPackageVersionsAsync(Guid? repositoryId, PackageVersionStatus? status,
        PageRequest page, CancellationToken cancellationToken);

    Task<Page<PackageVersion>> GetPackageVersionsAsync(PackageVersionListQuery query,
        CancellationToken cancellationToken);

    Task AddScanAsync(SecurityScan scan, IReadOnlyList<SecurityFinding> findings, IReadOnlyList<PolicyRuleResult> rules,
        CancellationToken cancellationToken);

    Task<Page<SecurityScan>> GetScansAsync(Guid packageVersionId, PageRequest page,
        CancellationToken cancellationToken);

    Task<Page<PolicyRuleResult>> GetRuleResultsAsync(Guid packageVersionId, PageRequest page,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicyRuleResult>> GetRuleResultsByIdsAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    Task<Page<SecurityFinding>> GetFindingsAsync(Guid? packageVersionId, FindingSeverity? minimumSeverity,
        PageRequest page, CancellationToken cancellationToken);

    Task AddApprovalAsync(PackageApproval approval, CancellationToken cancellationToken);

    Task AddApprovalRuleResultsAsync(Guid approvalId, IReadOnlyCollection<Guid> ruleResultIds,
        CancellationToken cancellationToken);

    Task<Page<PackageApproval>> GetApprovalsAsync(Guid packageVersionId, PageRequest page,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetApprovalRuleResultIdsAsync(
        IReadOnlyCollection<Guid> approvalIds, CancellationToken cancellationToken);

    Task AddAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    Task<Page<AuditEvent>> GetAuditEventsAsync(string? entityType, string? entityId, PageRequest page,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAuditEventEntityTypesAsync(CancellationToken cancellationToken);

    Task AddAccessTokenAsync(AccessToken token, CancellationToken cancellationToken);
    Task<AccessToken?> FindAccessTokenAsync(Guid id, CancellationToken cancellationToken);
    Task<AccessToken?> FindAccessTokenByTokenIdAsync(string tokenId, CancellationToken cancellationToken);
    Task<Page<AccessToken>> GetAccessTokensAsync(PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>>
        GetExpiredApprovalVersionIdsAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> TryAcquireJobLeaseAsync(string jobName, string owner, DateTimeOffset now, TimeSpan duration,
        CancellationToken cancellationToken);

    Task CompleteJobLeaseAsync(string jobName, string owner, DateTimeOffset now, string? error,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
    Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken);
    Task MigrateAsync(CancellationToken cancellationToken);
}

public interface IVulnerabilityCacheStore
{
    Task<VulnerabilityCacheEntry?> FindAsync(string provider, PackageType packageType, string normalizedName,
        string version, CancellationToken cancellationToken);

    Task StoreAsync(string provider, PackageType packageType, string normalizedName, string version, string payloadJson,
        DateTimeOffset fetchedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

public interface IPackageBlobStore
{
    Task<Stream?> OpenReadAsync(Guid packageVersionId, CancellationToken cancellationToken);

    Task StoreAsync(Guid packageVersionId, Stream content, string sha256, long maximumBytes,
        CancellationToken cancellationToken);

    Task<bool> DeleteUnapprovedAsync(Guid packageVersionId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid packageVersionId, string? sha256, CancellationToken cancellationToken);
}

public interface ILegacyPackageBlobMigrator
{
    Task<int> MigrateBatchAsync(int batchSize, CancellationToken cancellationToken);
}

public interface IPackageOperationLock
{
    Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken);
}

public interface IUpstreamClient
{
    PackageType PackageType { get; }

    Task<ResolvedArtifact?> ResolveExactAsync(Upstream upstream, string packageName, string version,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UpstreamPackageDto>> SearchAsync(Upstream upstream, string query, int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetVersionsAsync(Upstream upstream, string packageName,
        CancellationToken cancellationToken);
}

public interface IUpstreamResolver
{
    Task<ResolvedArtifact?> ResolveExactAsync(Guid repositoryId, PackageType packageType, string packageName,
        string version, CancellationToken cancellationToken);
}

public interface IUpstreamPackageSearch
{
    Task<IReadOnlyList<UpstreamPackageDto>> SearchAsync(Guid repositoryId, PackageType packageType, string query,
        int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetVersionsAsync(Guid repositoryId, Guid upstreamId, PackageType packageType,
        string packageName, CancellationToken cancellationToken);
}

public interface IPackageScanner
{
    bool Supports(PackageType packageType);

    Task<PackageInspectionResult> ScanAsync(PackageType packageType, Stream artifact,
        CancellationToken cancellationToken);
}

public interface IMalwareScanner
{
    Task<IReadOnlyList<ScanFinding>> ScanAsync(PackageType packageType, Stream artifact,
        CancellationToken cancellationToken);
}

public interface IVulnerabilityProvider
{
    string Name { get; }

    Task<IReadOnlyList<Vulnerability>> GetVulnerabilitiesAsync(PackageType packageType, string packageName,
        string version, CancellationToken cancellationToken);
}

public interface IPackagePolicyRule
{
    string Type { get; }

    Task<RuleEvaluation> EvaluateAsync(PolicyEvaluationContext context, Policy policy,
        CancellationToken cancellationToken);
}

public interface IPackagePolicyEvaluator
{
    Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken);
}

public interface IPackageAcquisitionCoordinator
{
    Task<ArtifactDelivery> GetOrAcquireAsync(ArtifactRequest request, CancellationToken cancellationToken);
    Task VerifyOriginAsync(Guid packageVersionId, CancellationToken cancellationToken);
}

public interface IAccessTokenService
{
    Task<CreatedAccessToken> CreateAsync(string name, string owner, IReadOnlyCollection<string> scopes,
        DateTimeOffset? expiresAt, CancellationToken cancellationToken);

    Task<bool> ValidateAsync(string token, Guid repositoryId, CancellationToken cancellationToken);
    Task RevokeAsync(Guid id, string actor, CancellationToken cancellationToken);
}

public interface IBackgroundJob
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task ExecuteAsync(CancellationToken cancellationToken);
}

public interface IBackgroundJobLease : IAsyncDisposable
{
    Task CompleteAsync(CancellationToken cancellationToken);
    Task FailAsync(string error, CancellationToken cancellationToken);
}

public interface IBackgroundJobLeaseProvider
{
    Task<IBackgroundJobLease?> TryAcquireAsync(string jobName, TimeSpan duration, CancellationToken cancellationToken);
}