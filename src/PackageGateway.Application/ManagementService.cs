using System.Text.Json;
using PackageGateway.Domain;

namespace PackageGateway.Application;

public sealed class GatewayManagementService(IGatewayStore store, IPackageBlobStore blobs)
{
    public async Task<RepositoryDto> CreateRepositoryAsync(CreateRepositoryCommand command, string actor,
        CancellationToken cancellationToken)
    {
        var repository = Repository.Create(command.Name, command.Slug, command.PackageType, command.Description);
        var policies = BalancedPolicyFactory.CreateFor(repository.PackageType);
        await store.AddRepositoryAsync(repository, policies, cancellationToken);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "RepositoryCreated", nameof(Repository), repository.Id.ToString(),
                $"Repository {repository.Slug} created."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(repository);
    }

    public async Task<RepositoryDto> UpdateRepositoryAsync(UpdateRepositoryCommand command, string actor,
        CancellationToken cancellationToken)
    {
        var repository = await store.FindRepositoryAsync(command.Id, cancellationToken) ??
                         throw new KeyNotFoundException("Repository not found.");
        repository.Update(command.Name, command.Description, command.Enabled);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "RepositoryUpdated", nameof(Repository), repository.Id.ToString(),
                $"Repository {repository.Slug} updated."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(repository);
    }

    public async Task<bool> DeleteRepositoryAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var repository = await store.FindRepositoryAsync(id, cancellationToken);
        if (repository is null) return false;
        repository.SoftDelete();
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "RepositoryDeleted", nameof(Repository), id.ToString(),
                "Repository soft-deleted; approved artifacts retained."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<UpstreamDto> CreateUpstreamAsync(CreateUpstreamCommand command, string actor,
        CancellationToken cancellationToken)
    {
        _ = await store.FindRepositoryAsync(command.RepositoryId, cancellationToken) ??
            throw new KeyNotFoundException("Repository not found.");
        var upstream = Upstream.Create(command.RepositoryId, command.Name, new Uri(command.Url), command.Priority,
            command.Trusted, command.PackageType);
        await store.AddUpstreamAsync(upstream, cancellationToken);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "UpstreamCreated", nameof(Upstream), upstream.Id.ToString(),
                $"Upstream {upstream.Name} created."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(upstream);
    }

    public async Task<UpstreamDto> UpdateUpstreamAsync(UpdateUpstreamCommand command, string actor,
        CancellationToken cancellationToken)
    {
        var upstream = await store.FindUpstreamAsync(command.Id, cancellationToken) ??
                       throw new KeyNotFoundException("Upstream not found.");
        upstream.Update(command.Name, new Uri(command.Url), command.Priority, command.Enabled, command.Trusted,
            command.PackageType);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "UpstreamUpdated", nameof(Upstream), upstream.Id.ToString(),
                $"Upstream {upstream.Name} updated."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(upstream);
    }

    public async Task<bool> DeleteUpstreamAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var upstream = await store.FindUpstreamAsync(id, cancellationToken);
        if (upstream is null) return false;
        upstream.SoftDelete();
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "UpstreamDeleted", nameof(Upstream), id.ToString(), "Upstream soft-deleted."),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PolicyDto> CreatePolicyAsync(CreatePolicyCommand command, string actor,
        CancellationToken cancellationToken)
    {
        PolicyConfiguration.Validate(command.Type, command.SchemaVersion, command.ConfigJson);
        var policy = Policy.Create(command.Name, command.Type, command.SchemaVersion, command.ConfigJson,
            command.PackageTypes);
        await store.AddPolicyAsync(policy, cancellationToken);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "PolicyCreated", nameof(Policy), policy.Id.ToString(),
                $"Policy {policy.Name} created."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<PolicyDto> UpdatePolicyAsync(UpdatePolicyCommand command, string actor,
        CancellationToken cancellationToken)
    {
        var policy = await store.FindPolicyAsync(command.Id, cancellationToken) ??
                     throw new KeyNotFoundException("Policy not found.");
        PolicyConfiguration.Validate(policy.Type, command.SchemaVersion, command.ConfigJson);
        policy.Update(command.Name, command.SchemaVersion, command.ConfigJson, command.Enabled, command.PackageTypes);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "PolicyUpdated", nameof(Policy), policy.Id.ToString(),
                $"Policy {policy.Name} updated."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<bool> DeletePolicyAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var policy = await store.FindPolicyAsync(id, cancellationToken);
        if (policy is null) return false;
        policy.SoftDelete();
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "PolicyDeleted", nameof(Policy), id.ToString(), "Policy soft-deleted."),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AssignPolicyAsync(Guid repositoryId, Guid policyId, bool assign, string actor,
        CancellationToken cancellationToken)
    {
        _ = await store.FindRepositoryAsync(repositoryId, cancellationToken) ??
            throw new KeyNotFoundException("Repository not found.");
        _ = await store.FindPolicyAsync(policyId, cancellationToken) ??
            throw new KeyNotFoundException("Policy not found.");
        if (assign) await store.AssignPolicyAsync(repositoryId, policyId, cancellationToken);
        else await store.UnassignPolicyAsync(repositoryId, policyId, cancellationToken);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, assign ? "PolicyAssigned" : "PolicyUnassigned", nameof(Repository),
                repositoryId.ToString(), $"Policy {policyId} {(assign ? "assigned" : "unassigned")}."),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<PackageVersionDto> DecideAsync(Guid id, ApprovalDecision decision, string reason, string actor,
        DateTimeOffset? expiresAt, IReadOnlyCollection<Guid>? affectedRuleResultIds,
        CancellationToken cancellationToken)
    {
        var version = await store.FindPackageVersionByIdAsync(id, cancellationToken) ??
                      throw new KeyNotFoundException("Package version not found.");
        if (expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("An approval or waiver expiration must be in the future.", nameof(expiresAt));
        IReadOnlyList<PolicyRuleResult> affected = [];
        if (decision == ApprovalDecision.WaivePolicy)
        {
            if (expiresAt is null || expiresAt <= DateTimeOffset.UtcNow)
                throw new ArgumentException("A waiver must have a future expiration.", nameof(expiresAt));
            if (affectedRuleResultIds is not { Count: > 0 })
                throw new ArgumentException("A waiver must identify at least one policy rule result.",
                    nameof(affectedRuleResultIds));
            affected = await store.GetRuleResultsByIdsAsync(affectedRuleResultIds, cancellationToken);
            if (affected.Count != affectedRuleResultIds.Distinct().Count() ||
                affected.Any(x => x.PackageVersionId != id))
                throw new ArgumentException("Every affected rule result must belong to the package version.",
                    nameof(affectedRuleResultIds));
            if (affected.Any(x => x.IsHardBlock))
                throw new InvalidOperationException("A non-waivable hard rule result cannot be waived.");
            if (affected.Any(x => x.Action is PolicyAction.Allow or PolicyAction.Warn))
                throw new ArgumentException("Only denying, quarantining, or manual-review rule results can be waived.",
                    nameof(affectedRuleResultIds));
        }

        if (decision == ApprovalDecision.Approve || decision == ApprovalDecision.WaivePolicy)
            version.ManuallyApprove(decision == ApprovalDecision.WaivePolicy);
        else version.ManuallyBlock();
        var approval = PackageApproval.Create(id, decision, reason, actor, expiresAt);
        await store.AddApprovalAsync(approval, cancellationToken);
        if (affected.Count > 0)
            await store.AddApprovalRuleResultsAsync(approval.Id, affected.Select(x => x.Id).ToArray(),
                cancellationToken);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, $"Package{decision}", nameof(PackageVersion), id.ToString(), reason),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(version);
    }

    public async Task<PackageVersionDto> RescanAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var version = await store.FindPackageVersionByIdAsync(id, cancellationToken) ??
                      throw new KeyNotFoundException("Package version not found.");
        version.QueueRescan();
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "PackageRescanQueued", nameof(PackageVersion), id.ToString(),
                "Package rescan queued."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(version);
    }

    public async Task<bool> RemovePackageVersionAsync(Guid id, string reason, string actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A removal reason is required.", nameof(reason));
        var version = await store.FindPackageVersionByIdAsync(id, cancellationToken) ??
                      throw new KeyNotFoundException("Package version not found.");
        var package = await store.FindPackageAsync(version.PackageId, cancellationToken) ??
                      throw new KeyNotFoundException("Package not found.");
        var description =
            $"Removed {package.Name} {version.Version} and its cached artifact and evaluation state. Reason: {reason.Trim()}";
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "PackageVersionRemoved", nameof(PackageVersion), id.ToString(), description),
            cancellationToken);
        if (!await store.RemovePackageVersionAsync(id, cancellationToken)) return false;
        await store.SaveChangesAsync(cancellationToken);
        await blobs.DeleteAsync(id, version.Sha256, cancellationToken);
        return true;
    }

    public async Task<PackageVersionDto> QuarantineAsync(Guid id, string reason, string actor,
        CancellationToken cancellationToken)
    {
        var version = await store.FindPackageVersionByIdAsync(id, cancellationToken) ??
                      throw new KeyNotFoundException("Package version not found.");
        version.ManuallyQuarantine();
        await store.AddApprovalAsync(PackageApproval.Create(id, ApprovalDecision.Reject, reason, actor),
            cancellationToken);
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "PackageQuarantined", nameof(PackageVersion), id.ToString(), reason),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(version);
    }

    public async Task<PackageVersionDto> RequireReviewAsync(Guid id, string reason, string actor,
        CancellationToken cancellationToken)
    {
        var version = await store.FindPackageVersionByIdAsync(id, cancellationToken) ??
                      throw new KeyNotFoundException("Package version not found.");
        version.ManuallyRequireReview();
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "PackageReviewRequired", nameof(PackageVersion), id.ToString(), reason),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(version);
    }

    public static RepositoryDto Map(Repository x)
    {
        return new RepositoryDto(x.Id, x.Name, x.Slug, x.PackageType, x.Enabled, x.Description, x.CreatedAt,
            x.UpdatedAt);
    }

    public static UpstreamDto Map(Upstream x)
    {
        return new UpstreamDto(x.Id, x.RepositoryId, x.Name, x.Url, x.PackageType, x.Priority, x.Enabled, x.Trusted,
            x.IsHealthy,
            x.LastHealthCheckAt, x.HealthDetail);
    }

    public static PolicyDto Map(Policy x)
    {
        return new PolicyDto(x.Id, x.Name, x.Type, x.SchemaVersion, x.ConfigJson, x.GetPackageTypes(), x.Enabled);
    }

    public static PackageDto Map(Package x)
    {
        return new PackageDto(x.Id, x.RepositoryId, x.Name, x.NormalizedName, x.PackageType);
    }

    public static PackageVersionDto Map(PackageVersion x)
    {
        return new PackageVersionDto(x.Id, x.PackageId, x.Version, x.Status, x.Sha256, x.Size, x.RiskScore,
            x.HasHardBlock,
            x.SignatureStatus, x.HasInstallScripts, x.License,
            x.HasHardBlock
                ? "A non-waivable security guard is active."
                : x.Status switch
                {
                    PackageVersionStatus.Approved => "The exact stored bytes are approved for delivery.",
                    PackageVersionStatus.ManualReview => "One or more policy rules require manual review.",
                    PackageVersionStatus.Quarantined => "The artifact is quarantined pending security review.",
                    PackageVersionStatus.Blocked => "Policy evaluation denied this artifact.",
                    PackageVersionStatus.Scanning => "Security evaluation is in progress.",
                    _ => "Security evaluation is pending."
                }, x.FirstSeenAt, x.LastScannedAt);
    }
}

public static class BalancedPolicyFactory
{
    public static IReadOnlyList<Policy> CreateFor(PackageType? type)
    {
        var all = Enum.GetValues<PackageType>();
        var policies = new List<Policy>
        {
            Policy.Create("Balanced vulnerabilities", "VulnerabilityPolicy", 1,
                "{\"critical\":\"Block\",\"high\":\"Block\",\"medium\":\"Warn\",\"low\":\"Allow\"}", all),
            Policy.Create("Balanced license policy", "LicensePolicy", 1,
                "{\"allowed\":[\"MIT\",\"Apache-2.0\"],\"manualReview\":[\"GPL-3.0\"],\"unknown\":\"Warn\"}", all),
            Policy.Create("Integrity guard", "IntegrityPolicy", 1,
                "{\"mismatch\":\"Block\",\"invalidSignature\":\"Block\",\"unsigned\":\"Warn\"}", all),
            Policy.Create("Explicit deny rules", "PackageDenyPolicy", 1, "{\"entries\":[]}", all),
            Policy.Create("Explicit allow rules", "PackageAllowPolicy", 1, "{\"entries\":[]}", all)
        };
        if (type is null or PackageType.NuGet)
            policies.Add(Policy.Create("24 hour NuGet cooldown", "CooldownPolicy", 1,
                "{\"hours\":24,\"action\":\"ManualReview\"}", [PackageType.NuGet]));
        if (type is null or PackageType.Npm)
        {
            policies.Add(Policy.Create("72 hour npm cooldown", "CooldownPolicy", 1,
                "{\"hours\":72,\"action\":\"ManualReview\"}", [PackageType.Npm]));
            policies.Add(Policy.Create("npm install scripts", "NpmInstallScriptPolicy", 1,
                "{\"action\":\"ManualReview\"}", [PackageType.Npm]));
        }

        return policies;
    }
}

public static class PolicyConfiguration
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        "VulnerabilityPolicy", "CooldownPolicy", "LicensePolicy", "PackageDenyPolicy", "PackageAllowPolicy",
        "IntegrityPolicy", "SignaturePolicy", "NpmInstallScriptPolicy"
    };

    public static void Validate(string type, int schemaVersion, string json)
    {
        if (!SupportedTypes.Contains(type))
            throw new ArgumentException($"Unsupported policy type '{type}'.", nameof(type));
        if (schemaVersion != 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Only policy schema version 1 is supported.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Policy configuration must be a JSON object.", nameof(json));
        var root = document.RootElement;
        switch (type)
        {
            case "CooldownPolicy":
                if (!root.TryGetProperty("hours", out var hours) || !hours.TryGetDouble(out var value) || value < 0 ||
                    value > 8760)
                    throw new ArgumentException("CooldownPolicy.hours must be between 0 and 8760.", nameof(json));
                RequireAction(root, "action");
                break;
            case "VulnerabilityPolicy":
                foreach (var name in new[] { "critical", "high", "medium", "low" }) RequireAction(root, name);
                break;
            case "LicensePolicy":
                RequireArray(root, "allowed");
                RequireArray(root, "manualReview");
                RequireAction(root, "unknown");
                break;
            case "PackageDenyPolicy" or "PackageAllowPolicy": RequireArray(root, "entries"); break;
            case "IntegrityPolicy":
                RequireAction(root, "mismatch");
                RequireAction(root, "invalidSignature");
                RequireAction(root, "unsigned");
                break;
            case "SignaturePolicy":
                RequireAction(root, "invalidSignature");
                RequireAction(root, "unsigned");
                break;
            case "NpmInstallScriptPolicy": RequireAction(root, "action"); break;
        }
    }

    private static void RequireAction(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<PolicyAction>(value.GetString(), true, out _))
            throw new ArgumentException($"Policy property '{property}' must contain a valid action.");
    }

    private static void RequireArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array ||
            value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
            throw new ArgumentException($"Policy property '{property}' must be a string array.");
    }
}