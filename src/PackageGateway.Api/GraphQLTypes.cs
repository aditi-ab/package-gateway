using System.Security.Claims;
using HotChocolate.Authorization;
using PackageGateway.Application;
using PackageGateway.Domain;
using PackageGateway.Infrastructure;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;

namespace PackageGateway.Api;

[ExtendObjectType<PackageVersionDto>]
public sealed class PackageVersionGraphQL
{
    public async Task<PackageDto?> GetPackage([Parent] PackageVersionDto version, [Service] IGatewayStore store,
        CancellationToken ct)
    {
        return await store.FindPackageAsync(version.PackageId, ct) is { } package
            ? GatewayManagementService.Map(package)
            : null;
    }
}

[ExtendObjectType<RepositoryDto>]
public sealed class RepositoryGraphQL
{
    [GraphQLDeprecated("Use packageTypes. Repository package type is retained only for compatibility.")]
    public PackageType? GetPackageType([Parent] RepositoryDto repository)
    {
        return repository.PackageType;
    }

    public async Task<IReadOnlyList<PackageType>> GetPackageTypes([Parent] RepositoryDto repository,
        [Service] IGatewayStore store, CancellationToken ct)
    {
        var upstreams = await store.GetUpstreamsAsync(repository.Id, null, ct);
        return upstreams.Select(x => x.PackageType).Concat(repository.PackageType is { } legacy ? [legacy] : [])
            .Distinct().Order().ToArray();
    }
}

public sealed class Query
{
    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<RepositoryDto>> GetRepositories(string? search, PackageType? packageType,
        bool? enabled, RepositorySortField? sortBy, SortDirection? direction, int? first, string? after,
        [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetRepositoriesAsync(
            new RepositoryListQuery(CursorPaging.Request(after, first), search, packageType, enabled,
                sortBy ?? RepositorySortField.Name, direction ?? SortDirection.Ascending), ct);
        return Map(page, GatewayManagementService.Map);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<RepositoryDto?> GetRepository(Guid id, [Service] IGatewayStore store, CancellationToken ct)
    {
        return await store.FindRepositoryAsync(id, ct) is { } item ? GatewayManagementService.Map(item) : null;
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<PackageDto>> GetPackages(Guid repositoryId, string? search, PackageSortField? sortBy,
        SortDirection? direction, int? first, string? after, [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetPackagesAsync(
            new PackageListQuery(repositoryId, CursorPaging.Request(after, first), search,
                sortBy ?? PackageSortField.Name, direction ?? SortDirection.Ascending), ct);
        return Map(page, GatewayManagementService.Map);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<PackageDto?> GetPackage(Guid id, [Service] IGatewayStore store, CancellationToken ct)
    {
        return await store.FindPackageAsync(id, ct) is { } item ? GatewayManagementService.Map(item) : null;
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<PackageVersionDto?> GetPackageVersion(Guid id, [Service] IGatewayStore store,
        CancellationToken ct)
    {
        return await store.FindPackageVersionByIdAsync(id, ct) is { } item ? GatewayManagementService.Map(item) : null;
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<PackageVersionDto>> GetPackageVersions(Guid? repositoryId, PackageType? packageType,
        PackageVersionStatus? status, string? packageName, PackageVersionSortField? sortBy, SortDirection? direction,
        int? first, string? after, [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetPackageVersionsAsync(
            new PackageVersionListQuery(CursorPaging.Request(after, first), repositoryId, packageType, status,
                packageName, sortBy ?? PackageVersionSortField.FirstSeenAt, direction ?? SortDirection.Descending), ct);
        return Map(page, GatewayManagementService.Map);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public Task<Connection<PackageVersionDto>> GetQuarantinedPackages(Guid? repositoryId, int? first, string? after,
        [Service] IGatewayStore store, CancellationToken ct)
    {
        return Versions(repositoryId, PackageVersionStatus.Quarantined, first, after, store, ct);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<FindingDto>> GetSecurityFindings(Guid? packageVersionId,
        FindingSeverity? minimumSeverity, int? first, string? after, [Service] IGatewayStore store,
        CancellationToken ct)
    {
        var page = await store.GetFindingsAsync(packageVersionId, minimumSeverity, CursorPaging.Request(after, first),
            ct);
        return Map(page,
            x => new FindingDto(x.Id, x.SecurityScanId, x.Type, x.Severity, x.Title, x.Description, x.Source,
                x.ExternalReference, x.IsHardBlock, x.RiskScore, x.CreatedAt));
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<SecurityScanDto>> GetScanHistory(Guid packageVersionId, int? first, string? after,
        [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetScansAsync(packageVersionId, CursorPaging.Request(after, first), ct);
        return Map(page,
            x => new SecurityScanDto(x.Id, x.PackageVersionId, x.StartedAt, x.CompletedAt, x.ScannerVersion, x.Result,
                x.RiskScore));
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<PolicyRuleResultDto>> GetPolicyRuleResults(Guid packageVersionId, int? first,
        string? after, [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetRuleResultsAsync(packageVersionId, CursorPaging.Request(after, first), ct);
        return Map(page,
            x => new PolicyRuleResultDto(x.Id, x.PackageVersionId, x.PolicyId, x.Rule, x.Action, x.Reason,
                x.IsHardBlock, x.EvaluatedAt));
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<PackageApprovalDto>> GetApprovalHistory(Guid packageVersionId, int? first,
        string? after, [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetApprovalsAsync(packageVersionId, CursorPaging.Request(after, first), ct);
        var links = await store.GetApprovalRuleResultIdsAsync(page.Items.Select(x => x.Id).ToArray(), ct);
        return Map(page,
            x => new PackageApprovalDto(x.Id, x.PackageVersionId, x.Decision, x.Reason, x.CreatedBy, x.CreatedAt,
                x.ExpiresAt, x.ProcessedAt, links.GetValueOrDefault(x.Id, [])));
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<PolicyDto>> GetPolicies(Guid? repositoryId, int? first, string? after,
        [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetPoliciesAsync(repositoryId, CursorPaging.Request(after, first), ct);
        return Map(page, GatewayManagementService.Map);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<IReadOnlyList<UpstreamDto>> GetUpstreams(Guid repositoryId, PackageType? packageType,
        [Service] IGatewayStore store, CancellationToken ct)
    {
        return (await store.GetUpstreamsAsync(repositoryId, packageType, ct)).Select(GatewayManagementService.Map)
            .ToArray();
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public Task<IReadOnlyList<UpstreamPackageDto>> GetUpstreamPackages(Guid repositoryId, PackageType packageType,
        string search, int? first, [Service] IUpstreamPackageSearch packages, CancellationToken ct)
    {
        return packages.SearchAsync(repositoryId, packageType, search, first ?? 25, ct);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public Task<IReadOnlyList<string>> GetUpstreamPackageVersions(Guid repositoryId, Guid upstreamId,
        PackageType packageType, string packageName, [Service] IUpstreamPackageSearch packages,
        CancellationToken ct)
    {
        return packages.GetVersionsAsync(repositoryId, upstreamId, packageType, packageName, ct);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<Connection<AuditEventDto>> GetAuditEvents(string? entityType, string? entityId, int? first,
        string? after, [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetAuditEventsAsync(entityType, entityId, CursorPaging.Request(after, first), ct);
        return Map(page,
            x => new AuditEventDto(x.Id, x.Timestamp, x.Actor, x.Action, x.EntityType, x.EntityId, x.Description,
                x.DataJson));
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public Task<IReadOnlyList<string>> GetAuditEventEntityTypes([Service] IGatewayStore store, CancellationToken ct)
    {
        return store.GetAuditEventEntityTypesAsync(ct);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<Connection<AccessTokenDto>> GetAccessTokens(int? first, string? after,
        [Service] IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetAccessTokensAsync(CursorPaging.Request(after, first), ct);
        return Map(page, AccessTokenService.ToDto);
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public IReadOnlyList<JobStatus> GetScanners([Service] JobHealthRegistry health)
    {
        return health.GetAll();
    }

    [Authorize(Policy = AuthorizationPolicies.Reader)]
    public async Task<SystemStatusDto> GetSystemStatus([Service] IGatewayStore store,
        [Service] JobHealthRegistry health, [Service] DependencyHealthRegistry dependencies, CancellationToken ct)
    {
        var connected = await store.CanConnectAsync(ct);
        var pending = connected && await store.HasPendingMigrationsAsync(ct);
        var jobs = health.GetAll();
        return new SystemStatusDto(typeof(Query).Assembly.GetName().Version?.ToString() ?? "1.0.0", Program.StartedAt,
            new ComponentStatusDto("database", connected && !pending,
                !connected ? "Unavailable" : pending ? "Pending migrations" : null),
            new ComponentStatusDto("backgroundScanner", jobs.All(x => x.Healthy),
                jobs.FirstOrDefault(x => !x.Healthy)?.Detail),
            dependencies.GetAll().Select(x =>
                new ComponentStatusDto(x.Name, x.Healthy,
                    x.UsingCachedData ? $"Cached fallback: {x.Detail}" : x.Detail)).ToArray());
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IReadOnlyList<LocalUserDto>> GetLocalUsers(
        [Service] LocalAuthenticationService authentication, CancellationToken ct)
    {
        return (await authentication.ListAsync(ct)).Select(LocalUserDto.From).ToArray();
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public IReadOnlyList<string> GetLocalRoleCatalog()
    {
        return LocalAuthenticationService.Roles;
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public EntraConnectionDto GetEntraConnection([Service] EntraConnectionService connection)
    {
        return EntraConnectionDto.From(connection.Get());
    }

    private static async Task<Connection<PackageVersionDto>> Versions(Guid? repositoryId, PackageVersionStatus status,
        int? first, string? after, IGatewayStore store, CancellationToken ct)
    {
        var page = await store.GetPackageVersionsAsync(repositoryId, status, CursorPaging.Request(after, first), ct);
        return Map(page, GatewayManagementService.Map);
    }

    private static Connection<TTarget> Map<TSource, TTarget>(Page<TSource> page, Func<TSource, TTarget> map)
    {
        return CursorPaging.ToConnection(new Page<TTarget>(page.Items.Select(map).ToArray(), page.TotalCount,
            page.Offset, page.Limit));
    }
}

public sealed class Mutation
{
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<LocalUserSecretPayload> CreateLocalUser(string username, IReadOnlyList<string> roles,
        [Service] LocalAuthenticationService authentication, [Service] IHttpContextAccessor context,
        CancellationToken ct)
    {
        var result = await authentication.CreateAsync(username, roles, Actor(context), ct);
        return new LocalUserSecretPayload(LocalUserDto.From(result.User), result.TemporaryPassword, []);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<LocalUserPayload> UpdateLocalUser(Guid id, Guid expectedVersion, IReadOnlyList<string> roles,
        bool enabled, [Service] LocalAuthenticationService authentication, [Service] IHttpContextAccessor context,
        CancellationToken ct)
    {
        return new LocalUserPayload(LocalUserDto.From(await authentication.UpdateAsync(id, expectedVersion, roles,
            enabled, Actor(context), ct)), []);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<LocalUserSecretPayload> ResetLocalUserPassword(Guid id,
        [Service] LocalAuthenticationService authentication, [Service] IHttpContextAccessor context,
        CancellationToken ct)
    {
        var result = await authentication.ResetAsync(id, Actor(context), ct);
        return new LocalUserSecretPayload(LocalUserDto.From(result.User), result.TemporaryPassword, []);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<BooleanPayload> DeleteLocalUser(Guid id, [Service] LocalAuthenticationService authentication,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        await authentication.DeleteAsync(id, Actor(context), ct);
        return new BooleanPayload(true, []);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<EntraConnectionDto> UpdateEntraConnection(bool enabled, string authority, string audience,
        string clientId, string scope, Guid expectedVersion, [Service] EntraConnectionService connection,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return EntraConnectionDto.From(await connection.UpdateAsync(enabled, authority, audience, clientId, scope,
            expectedVersion, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<RepositoryPayload> CreateRepository(CreateRepositoryCommand input,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapRepository(() => management.CreateRepositoryAsync(input, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<RepositoryPayload> UpdateRepository(UpdateRepositoryCommand input,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapRepository(() => management.UpdateRepositoryAsync(input, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<BooleanPayload> DeleteRepository(Guid id, [Service] GatewayManagementService management,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapBoolean(() => management.DeleteRepositoryAsync(id, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<UpstreamPayload> CreateUpstream(CreateUpstreamCommand input,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapUpstream(() => management.CreateUpstreamAsync(input, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<UpstreamPayload> UpdateUpstream(UpdateUpstreamCommand input,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapUpstream(() => management.UpdateUpstreamAsync(input, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<BooleanPayload> DeleteUpstream(Guid id, [Service] GatewayManagementService management,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapBoolean(() => management.DeleteUpstreamAsync(id, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<PolicyPayload> CreatePolicy(CreatePolicyCommand input, [Service] GatewayManagementService management,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapPolicy(() => management.CreatePolicyAsync(input, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<PolicyPayload> UpdatePolicy(UpdatePolicyCommand input, [Service] GatewayManagementService management,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapPolicy(() => management.UpdatePolicyAsync(input, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<BooleanPayload> DeletePolicy(Guid id, [Service] GatewayManagementService management,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapBoolean(() => management.DeletePolicyAsync(id, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<BooleanPayload> AssignPolicy(Guid repositoryId, Guid policyId,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapAction(async () =>
            await management.AssignPolicyAsync(repositoryId, policyId, true, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public Task<BooleanPayload> UnassignPolicy(Guid repositoryId, Guid policyId,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapAction(async () =>
            await management.AssignPolicyAsync(repositoryId, policyId, false, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.SecurityReviewer)]
    public Task<PackageVersionPayload> ApprovePackageVersion(Guid id, string? reason, DateTimeOffset? expiresAt,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapVersion(() => management.DecideAsync(id, ApprovalDecision.Approve, reason ?? "Manual approval.",
            Actor(context), expiresAt, null, ct));
    }

    [Authorize(Policy = AuthorizationPolicies.SecurityReviewer)]
    public Task<PackageVersionPayload> WaivePackageVersion(Guid id, IReadOnlyList<Guid> affectedRuleResultIds,
        string reason, DateTimeOffset expiresAt, [Service] GatewayManagementService management,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapVersion(() => management.DecideAsync(id, ApprovalDecision.WaivePolicy, reason, Actor(context),
            expiresAt, affectedRuleResultIds, ct));
    }

    [Authorize(Policy = AuthorizationPolicies.SecurityReviewer)]
    public Task<PackageVersionPayload> BlockPackageVersion(Guid id, string reason,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapVersion(() =>
            management.DecideAsync(id, ApprovalDecision.Reject, reason, Actor(context), null, null, ct));
    }

    [Authorize(Policy = AuthorizationPolicies.SecurityReviewer)]
    public Task<PackageVersionPayload> QuarantinePackageVersion(Guid id, string reason,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapVersion(() => management.QuarantineAsync(id, reason, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.SecurityReviewer)]
    public Task<PackageVersionPayload> RequirePackageVersionReview(Guid id, string reason,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapVersion(() => management.RequireReviewAsync(id, reason, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.SecurityReviewer)]
    public Task<PackageVersionPayload> RescanPackageVersion(Guid id, [Service] GatewayManagementService management,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapVersion(() => management.RescanAsync(id, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public async Task<PackageVersionPayload> AddPackageVersion(Guid repositoryId, PackageType packageType,
        string packageName, string version, [Service] IGatewayStore store, [Service] IUpstreamResolver resolver,
        [Service] IPackageAcquisitionCoordinator coordinator, [Service] IHttpContextAccessor context,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Package name is required.", nameof(packageName));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version is required.", nameof(version));
            var repository = await store.FindRepositoryAsync(repositoryId, ct) ??
                             throw new KeyNotFoundException("Repository not found.");
            if (!repository.Enabled) throw new InvalidOperationException("Repository is disabled.");
            var source = await resolver.ResolveExactAsync(repositoryId, packageType, packageName.Trim(), version.Trim(),
                ct) ?? throw new KeyNotFoundException("Package version was not found on an enabled upstream.");
            var delivery = await coordinator.GetOrAcquireAsync(
                new ArtifactRequest(repository.Id, repository.Slug, packageType, packageName.Trim(), version.Trim(),
                    source.UpstreamId, source.ArtifactUri, source.PublishedAt, source.ExpectedSha256,
                    source.ExpectedIntegrity), ct);
            if (delivery.Content is not null) await delivery.Content.DisposeAsync();
            var stored = await store.FindPackageVersionAsync(repositoryId, packageType,
                PackageIdentity.Normalize(packageName, packageType), version.Trim(), ct);
            if (stored is null)
                throw new InvalidOperationException(delivery.Message ?? "Package acquisition did not start.");
            await store.AddAuditAsync(
                AuditEvent.Create(Actor(context), "PackageProactivelyAdded", nameof(PackageVersion),
                    stored.Value.Version.Id.ToString(),
                    $"Proactive acquisition requested for {stored.Value.Package.Name} {stored.Value.Version.Version}."),
                ct);
            await store.SaveChangesAsync(ct);
            return new PackageVersionPayload(GatewayManagementService.Map(stored.Value.Version), []);
        }
        catch (Exception ex)
        {
            return new PackageVersionPayload(null, [Error(ex)]);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public Task<BooleanPayload> RemovePackageVersion(Guid id, string reason,
        [Service] GatewayManagementService management, [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        return WrapBoolean(() => management.RemovePackageVersionAsync(id, reason, Actor(context), ct));
    }

    [Authorize(Policy = AuthorizationPolicies.RepositoryAdmin)]
    public async Task<BooleanPayload> ClearPackageCache(Guid repositoryId, string? packageName,
        PackageType? packageType, [Service] IGatewayStore store, [Service] IPackageBlobStore blobs,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        try
        {
            var repository = await store.FindRepositoryAsync(repositoryId, ct) ??
                             throw new KeyNotFoundException("Repository not found.");
            var deleted = false;
            var offset = 0;
            Page<PackageVersion> versions;
            do
            {
                versions = await store.GetPackageVersionsAsync(repositoryId, null, new PageRequest(offset, 100), ct);
                foreach (var version in versions.Items)
                {
                    if (packageName is not null || packageType is not null)
                    {
                        var package = await store.FindPackageAsync(version.PackageId, ct);
                        if (package is null || (packageType is not null && package.PackageType != packageType))
                            continue;
                        if (packageName is not null && !string.Equals(package.NormalizedName,
                                PackageIdentity.Normalize(packageName, package.PackageType),
                                StringComparison.Ordinal)) continue;
                    }

                    if (!await blobs.DeleteUnapprovedAsync(version.Id, ct)) continue;
                    deleted = true;
                    await store.AddAuditAsync(
                        AuditEvent.Create(Actor(context), "PackageCacheCleared", nameof(PackageVersion),
                            version.Id.ToString(), "Unapproved cached artifact deleted."), ct);
                }

                offset += versions.Items.Count;
            } while (offset < versions.TotalCount && versions.Items.Count > 0);

            await store.SaveChangesAsync(ct);
            return new BooleanPayload(deleted, []);
        }
        catch (Exception ex)
        {
            return new BooleanPayload(false, [Error(ex)]);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<AccessTokenPayload> CreateAccessToken(string name, IReadOnlyList<string> scopes,
        DateTimeOffset? expiresAt, [Service] IAccessTokenService tokens, [Service] IHttpContextAccessor context,
        CancellationToken ct)
    {
        try
        {
            return new AccessTokenPayload(await tokens.CreateAsync(name, Actor(context), scopes, expiresAt, ct), []);
        }
        catch (Exception ex)
        {
            return new AccessTokenPayload(null, [Error(ex)]);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<BooleanPayload> RevokeAccessToken(Guid id, [Service] IAccessTokenService tokens,
        [Service] IHttpContextAccessor context, CancellationToken ct)
    {
        try
        {
            await tokens.RevokeAsync(id, Actor(context), ct);
            return new BooleanPayload(true, []);
        }
        catch (Exception ex)
        {
            return new BooleanPayload(false, [Error(ex)]);
        }
    }

    private static async Task<RepositoryPayload> WrapRepository(Func<Task<RepositoryDto>> action)
    {
        try
        {
            return new RepositoryPayload(await action(), []);
        }
        catch (Exception ex)
        {
            return new RepositoryPayload(null, [Error(ex)]);
        }
    }

    private static async Task<UpstreamPayload> WrapUpstream(Func<Task<UpstreamDto>> action)
    {
        try
        {
            return new UpstreamPayload(await action(), []);
        }
        catch (Exception ex)
        {
            return new UpstreamPayload(null, [Error(ex)]);
        }
    }

    private static async Task<PolicyPayload> WrapPolicy(Func<Task<PolicyDto>> action)
    {
        try
        {
            return new PolicyPayload(await action(), []);
        }
        catch (Exception ex)
        {
            return new PolicyPayload(null, [Error(ex)]);
        }
    }

    private static async Task<PackageVersionPayload> WrapVersion(Func<Task<PackageVersionDto>> action)
    {
        try
        {
            return new PackageVersionPayload(await action(), []);
        }
        catch (Exception ex)
        {
            return new PackageVersionPayload(null, [Error(ex)]);
        }
    }

    private static async Task<BooleanPayload> WrapBoolean(Func<Task<bool>> action)
    {
        try
        {
            return new BooleanPayload(await action(), []);
        }
        catch (Exception ex)
        {
            return new BooleanPayload(false, [Error(ex)]);
        }
    }

    private static async Task<BooleanPayload> WrapAction(Func<Task> action)
    {
        try
        {
            await action();
            return new BooleanPayload(true, []);
        }
        catch (Exception ex)
        {
            return new BooleanPayload(false, [Error(ex)]);
        }
    }

    private static ApiError Error(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => new ApiError("NOT_FOUND", ex.Message),
            ArgumentException => new ApiError("VALIDATION", ex.Message),
            InvalidOperationException => new ApiError("CONFLICT", ex.Message),
            _ => new ApiError("INTERNAL",
                "The operation failed unexpectedly. Use the audit identifier from server logs when contacting an administrator.")
        };
    }

    private static string Actor(IHttpContextAccessor context)
    {
        return context.HttpContext?.User.FindFirstValue("preferred_username") ??
               context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    }
}

public sealed record ApiError(string Code, string Message);

public sealed record RepositoryPayload(RepositoryDto? Repository, IReadOnlyList<ApiError> Errors);

public sealed record UpstreamPayload(UpstreamDto? Upstream, IReadOnlyList<ApiError> Errors);

public sealed record PolicyPayload(PolicyDto? Policy, IReadOnlyList<ApiError> Errors);

public sealed record PackageVersionPayload(PackageVersionDto? PackageVersion, IReadOnlyList<ApiError> Errors);

public sealed record BooleanPayload(bool Success, IReadOnlyList<ApiError> Errors);

public sealed record AccessTokenPayload(CreatedAccessToken? AccessToken, IReadOnlyList<ApiError> Errors);

public sealed record LocalUserDto(
    Guid Id,
    string Username,
    IReadOnlyList<string> Roles,
    bool Enabled,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    Guid Version)
{
    public static LocalUserDto From(LocalAdministrator user)
    {
        return new LocalUserDto(user.Id, user.Username,
            LocalAuthenticationService.UserRoles(user), user.Enabled, user.MustChangePassword, user.CreatedAt,
            user.LastLoginAt, user.ConcurrencyToken);
    }
}

public sealed record LocalUserPayload(LocalUserDto? User, IReadOnlyList<ApiError> Errors);

public sealed record EntraConnectionDto(
    bool Enabled,
    bool Configured,
    string Authority,
    string Audience,
    string ClientId,
    string Scope,
    Guid Version)
{
    public static EntraConnectionDto From(EntraConnectionSnapshot value)
    {
        return new EntraConnectionDto(value.Enabled, value.Configured, value.Authority, value.Audience, value.ClientId,
            value.Scope,
            value.Version);
    }
}

public sealed record LocalUserSecretPayload(
    LocalUserDto User,
    string TemporaryPassword,
    IReadOnlyList<ApiError> Errors);