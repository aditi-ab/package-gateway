using System.Security.Cryptography;

namespace PackageGateway.Domain;

public enum PackageType
{
    NuGet,
    Npm
}

public enum PackageVersionStatus
{
    Unknown,
    Pending,
    Scanning,
    Approved,
    ManualReview,
    Quarantined,
    Blocked
}

public enum PolicyAction
{
    Allow,
    Warn,
    ManualReview,
    Quarantine,
    Block
}

public enum FindingSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

public enum SignatureStatus
{
    Unknown,
    Unsigned,
    Valid,
    Invalid
}

public enum ScanResult
{
    Succeeded,
    Failed,
    TimedOut
}

public enum ApprovalDecision
{
    Approve,
    Reject,
    WaivePolicy
}

public sealed class LocalAdministrator
{
    private LocalAdministrator()
    {
    }

    private LocalAdministrator(string username, string normalizedUsername, string passwordHash,
        IReadOnlyList<string>? roles = null)
    {
        Id = Guid.CreateVersion7();
        Username = Repository.Required(username, nameof(username), 100);
        NormalizedUsername = Repository.Required(normalizedUsername, nameof(normalizedUsername), 100);
        PasswordHash = Repository.Required(passwordHash, nameof(passwordHash), 1000);
        Roles = string.Join(',', roles ?? ["Administrator"]);
        SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string NormalizedUsername { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Roles { get; private set; } = "Administrator";
    public bool Enabled { get; private set; } = true;
    public bool MustChangePassword { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public string? DisplayName { get; private set; }
    public string ExternalIdentitiesJson { get; private set; } = "[]";
    public string RoleGrantsJson { get; private set; } = "[]";

    public void ApplyIdentityState(Guid id, string? displayName, string passwordHash, IReadOnlyList<string> roles, bool enabled,
        bool mustChangePassword, string securityStamp, Guid concurrencyToken, DateTimeOffset? lastLoginAt,
        string externalIdentitiesJson, string roleGrantsJson)
    {
        Id = id;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Roles = string.Join(',', roles);
        Enabled = enabled;
        MustChangePassword = mustChangePassword;
        SecurityStamp = securityStamp;
        ConcurrencyToken = concurrencyToken;
        LastLoginAt = lastLoginAt;
        ExternalIdentitiesJson = externalIdentitiesJson;
        RoleGrantsJson = roleGrantsJson;
    }

    public static LocalAdministrator Create(string username, string normalizedUsername, string passwordHash,
        IReadOnlyList<string>? roles = null)
    {
        return new LocalAdministrator(username, normalizedUsername, passwordHash, roles);
    }

    public void UpdatePasswordHash(string passwordHash)
    {
        PasswordHash = Repository.Required(passwordHash, nameof(passwordHash), 1000);
        SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ConcurrencyToken = Guid.NewGuid();
    }

    public void SetPassword(string passwordHash, bool mustChange)
    {
        UpdatePasswordHash(passwordHash);
        MustChangePassword = mustChange;
    }

    public void SetAccess(IReadOnlyList<string> roles, bool enabled)
    {
        Roles = string.Join(',', roles);
        Enabled = enabled;
        SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ConcurrencyToken = Guid.NewGuid();
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
    }
}

public sealed class AdminIdentityProviderDocument
{
    public string Id { get; private set; } = string.Empty;
    public string Json { get; private set; } = string.Empty;

    public static AdminIdentityProviderDocument Create(string id, string json) => new() { Id = id, Json = json };
    public void Update(string json) => Json = json;
}

public sealed class Repository
{
    private Repository()
    {
    }

    private Repository(string name, string slug, PackageType? packageType, string? description)
    {
        Id = Guid.CreateVersion7();
        Name = Required(name, nameof(name), 200);
        Slug = NormalizeSlug(slug);
        PackageType = packageType;
        Description = description?.Trim();
        Enabled = true;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public PackageType? PackageType { get; private set; }
    public bool Enabled { get; private set; }
    public string? Description { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public static Repository Create(string name, string slug, PackageType? packageType = null,
        string? description = null)
    {
        return new Repository(name, slug, packageType, description);
    }

    public void Update(string name, string? description, bool enabled)
    {
        Name = Required(name, nameof(name), 200);
        Description = description?.Trim();
        Enabled = enabled;
        Touch();
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Enabled = false;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal static string Required(string value, string name, int max)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        value = value.Trim();
        if (value.Length > max) throw new ArgumentOutOfRangeException(name, $"Maximum length is {max}.");
        return value;
    }

    public static string NormalizeSlug(string value)
    {
        value = Required(value, nameof(value), 100).ToLowerInvariant();
        if (value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')) || value[0] == '-' || value[^1] == '-')
            throw new ArgumentException("A slug must contain lowercase letters, digits, and interior hyphens only.",
                nameof(value));
        return value;
    }
}

public sealed class EntraConnectionSettings
{
    public Guid Id { get; private set; } = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public bool Enabled { get; private set; }
    public string Authority { get; private set; } = string.Empty;
    public string Audience { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public void Update(bool enabled, string authority, string audience, string clientId, string scope)
    {
        Enabled = enabled;
        Authority = authority;
        Audience = audience;
        ClientId = clientId;
        Scope = scope;
        ConcurrencyToken = Guid.NewGuid();
    }
}

public sealed class Upstream
{
    private Upstream()
    {
    }

    private Upstream(Guid repositoryId, string name, Uri url, int priority, bool trusted, PackageType packageType)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Upstreams must use HTTPS.", nameof(url));
        Id = Guid.CreateVersion7();
        RepositoryId = repositoryId;
        Name = Repository.Required(name, nameof(name), 200);
        Url = url.AbsoluteUri.TrimEnd('/');
        Priority = priority;
        Trusted = trusted;
        PackageType = packageType;
        Enabled = true;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public PackageType PackageType { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public bool Trusted { get; private set; }
    public bool IsDeleted { get; private set; }
    public bool? IsHealthy { get; private set; }
    public DateTimeOffset? LastHealthCheckAt { get; private set; }
    public string? HealthDetail { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public static Upstream Create(Guid repositoryId, string name, Uri url, int priority, bool trusted = false,
        PackageType packageType = PackageType.NuGet)
    {
        return new Upstream(repositoryId, name, url, priority, trusted, packageType);
    }

    public void Update(string name, Uri url, int priority, bool enabled, bool trusted, PackageType packageType)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Upstreams must use HTTPS.", nameof(url));
        Name = Repository.Required(name, nameof(name), 200);
        Url = url.AbsoluteUri.TrimEnd('/');
        Priority = priority;
        Enabled = enabled;
        Trusted = trusted;
        PackageType = packageType;
        Touch();
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Enabled = false;
        Touch();
    }

    public void RecordHealth(bool healthy, string? detail)
    {
        IsHealthy = healthy;
        LastHealthCheckAt = DateTimeOffset.UtcNow;
        HealthDetail = detail is { Length: > 2000 } ? detail[..2000] : detail;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }
}

public sealed class Package
{
    private Package()
    {
    }

    private Package(Guid repositoryId, string name, PackageType type)
    {
        Id = Guid.CreateVersion7();
        RepositoryId = repositoryId;
        Name = Repository.Required(name, nameof(name), 300);
        NormalizedName = PackageIdentity.Normalize(name, type);
        PackageType = type;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; } = string.Empty;
    public PackageType PackageType { get; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Package Create(Guid repositoryId, string name, PackageType type)
    {
        return new Package(repositoryId, name, type);
    }

    public void UpdateDisplayName(string name)
    {
        name = Repository.Required(name, nameof(name), 300);
        if (!StringComparer.Ordinal.Equals(NormalizedName, PackageIdentity.Normalize(name, PackageType)))
            throw new InvalidOperationException(
                "The display name must represent the same normalized package identity.");
        Name = name;
    }
}

public sealed class PackageVersion
{
    private static readonly IReadOnlyDictionary<PackageVersionStatus, PackageVersionStatus[]> AllowedTransitions =
        new Dictionary<PackageVersionStatus, PackageVersionStatus[]>
        {
            [PackageVersionStatus.Unknown] = [PackageVersionStatus.Pending],
            [PackageVersionStatus.Pending] = [PackageVersionStatus.Scanning, PackageVersionStatus.Blocked],
            [PackageVersionStatus.Scanning] =
            [
                PackageVersionStatus.Approved, PackageVersionStatus.ManualReview, PackageVersionStatus.Quarantined,
                PackageVersionStatus.Blocked, PackageVersionStatus.Pending
            ],
            [PackageVersionStatus.Approved] =
            [
                PackageVersionStatus.Pending, PackageVersionStatus.ManualReview, PackageVersionStatus.Blocked,
                PackageVersionStatus.Quarantined
            ],
            [PackageVersionStatus.ManualReview] =
            [
                PackageVersionStatus.Approved, PackageVersionStatus.Blocked, PackageVersionStatus.Quarantined,
                PackageVersionStatus.Pending
            ],
            [PackageVersionStatus.Quarantined] =
                [PackageVersionStatus.Approved, PackageVersionStatus.Blocked, PackageVersionStatus.Pending],
            [PackageVersionStatus.Blocked] = [PackageVersionStatus.Pending, PackageVersionStatus.Approved]
        };

    private PackageVersion()
    {
    }

    private PackageVersion(Guid packageId, string version, Guid upstreamId, string artifactUrl,
        DateTimeOffset? publishedAt, string? expectedSha256, string? expectedIntegrity)
    {
        Id = Guid.CreateVersion7();
        PackageId = packageId;
        Version = Repository.Required(version, nameof(version), 100);
        UpstreamId = upstreamId;
        ArtifactUrl = Repository.Required(artifactUrl, nameof(artifactUrl), 4000);
        PublishedAt = publishedAt;
        ExpectedSha256 = NormalizeOptional(expectedSha256, 64);
        ExpectedIntegrity = NormalizeOptional(expectedIntegrity, 1000);
        FirstSeenAt = CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        Status = PackageVersionStatus.Unknown;
        ConcurrencyToken = Guid.NewGuid();
        TransitionTo(PackageVersionStatus.Pending);
    }

    public Guid Id { get; private set; }
    public Guid PackageId { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public PackageVersionStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset? LastScannedAt { get; private set; }
    public string? Sha256 { get; private set; }
    public long? Size { get; private set; }
    public Guid UpstreamId { get; private set; }
    public string ArtifactUrl { get; private set; } = string.Empty;
    public string? ExpectedSha256 { get; private set; }
    public string? ExpectedIntegrity { get; private set; }
    public string? License { get; private set; }
    public string? Author { get; private set; }
    public string? Publisher { get; private set; }
    public bool HasInstallScripts { get; private set; }
    public SignatureStatus SignatureStatus { get; private set; }
    public int RiskScore { get; private set; }
    public bool HasHardBlock { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public bool CanBeDelivered => Status == PackageVersionStatus.Approved && Sha256 is not null && Size is not null;

    public static PackageVersion Create(Guid packageId, string version, Guid upstreamId, string artifactUrl,
        DateTimeOffset? publishedAt = null, string? expectedSha256 = null, string? expectedIntegrity = null)
    {
        return new PackageVersion(packageId, version, upstreamId, artifactUrl, publishedAt, expectedSha256,
            expectedIntegrity);
    }

    public void BeginScan()
    {
        TransitionTo(PackageVersionStatus.Scanning);
    }

    public void CompleteScan(PackageVersionStatus status, int riskScore, bool hardBlock,
        SignatureStatus signatureStatus, bool hasInstallScripts, string? license)
    {
        if (status is not (PackageVersionStatus.Approved or PackageVersionStatus.ManualReview
            or PackageVersionStatus.Quarantined or PackageVersionStatus.Blocked))
            throw new ArgumentOutOfRangeException(nameof(status));
        RiskScore = Math.Max(0, riskScore);
        HasHardBlock = hardBlock;
        SignatureStatus = signatureStatus;
        HasInstallScripts = hasInstallScripts;
        License = license;
        LastScannedAt = DateTimeOffset.UtcNow;
        TransitionTo(status);
    }

    public void SetArtifact(string sha256, long size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (Sha256 is not null && !StringComparer.OrdinalIgnoreCase.Equals(Sha256, sha256))
            throw new InvalidOperationException("Immutable artifact hash conflict.");
        Sha256 = sha256.ToLowerInvariant();
        Size = size;
        Touch();
    }

    public void QueueRescan()
    {
        TransitionTo(PackageVersionStatus.Pending);
    }

    public void ManuallyApprove(bool policyWaiver = false)
    {
        if (HasHardBlock) throw new InvalidOperationException("A hard security guard cannot be waived.");
        if (Status == PackageVersionStatus.Blocked && !policyWaiver)
            throw new InvalidOperationException("A blocked version requires an explicit policy waiver.");
        TransitionTo(PackageVersionStatus.Approved);
    }

    public void ManuallyBlock()
    {
        TransitionTo(PackageVersionStatus.Blocked);
    }

    public void ManuallyRequireReview()
    {
        TransitionTo(PackageVersionStatus.ManualReview);
    }

    public void ApplyHardBlock(int riskScore = 100)
    {
        HasHardBlock = true;
        RiskScore = Math.Max(RiskScore, riskScore);
        if (Status == PackageVersionStatus.Blocked) Touch();
        else TransitionTo(PackageVersionStatus.Blocked);
    }

    public void ManuallyQuarantine()
    {
        TransitionTo(PackageVersionStatus.Quarantined);
    }

    private void TransitionTo(PackageVersionStatus next)
    {
        if (!AllowedTransitions[Status].Contains(next))
            throw new InvalidOperationException($"Invalid package state transition: {Status} -> {next}.");
        Status = next;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= maxLength ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public static class PackageIdentity
{
    public static string Normalize(string name, PackageType type)
    {
        name = Repository.Required(name, nameof(name), 300).Trim();
        return type switch
        {
            PackageType.NuGet => name.ToLowerInvariant(),
            PackageType.Npm when name.StartsWith('@') && name.Contains('/') => name.ToLowerInvariant(),
            PackageType.Npm when !name.Contains('/') => name.ToLowerInvariant(),
            _ => throw new ArgumentException("Invalid npm package name.", nameof(name))
        };
    }
}

public sealed class Policy
{
    private Policy()
    {
    }

    private Policy(string name, string type, int schemaVersion, string configJson,
        IEnumerable<PackageType>? packageTypes)
    {
        Id = Guid.CreateVersion7();
        Name = Repository.Required(name, nameof(name), 200);
        Type = Repository.Required(type, nameof(type), 100);
        if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        SchemaVersion = schemaVersion;
        ConfigJson = Repository.Required(configJson, nameof(configJson), 32_000);
        PackageTypes = SerializePackageTypes(packageTypes);
        Enabled = true;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public string ConfigJson { get; private set; } = string.Empty;
    public string PackageTypes { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public static Policy Create(string name, string type, int schemaVersion, string configJson,
        IEnumerable<PackageType>? packageTypes = null)
    {
        return new Policy(name, type, schemaVersion, configJson, packageTypes);
    }

    public void Update(string name, int schemaVersion, string configJson, bool enabled,
        IEnumerable<PackageType>? packageTypes = null)
    {
        Name = Repository.Required(name, nameof(name), 200);
        SchemaVersion = schemaVersion;
        ConfigJson = Repository.Required(configJson, nameof(configJson), 32_000);
        Enabled = enabled;
        PackageTypes = SerializePackageTypes(packageTypes);
        Touch();
    }

    public IReadOnlySet<PackageType> GetPackageTypes()
    {
        return PackageTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<PackageType>)
            .ToHashSet();
    }

    public bool AppliesTo(PackageType packageType)
    {
        return GetPackageTypes().Contains(packageType);
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Enabled = false;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string SerializePackageTypes(IEnumerable<PackageType>? packageTypes)
    {
        var values = (packageTypes ?? Enum.GetValues<PackageType>()).Distinct().Order().ToArray();
        if (values.Length == 0)
            throw new ArgumentException("At least one package type is required.", nameof(packageTypes));
        return string.Join(',', values);
    }
}

public sealed class RepositoryPolicy
{
    private RepositoryPolicy()
    {
    }

    private RepositoryPolicy(Guid repositoryId, Guid policyId)
    {
        RepositoryId = repositoryId;
        PolicyId = policyId;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public Guid RepositoryId { get; private set; }
    public Guid PolicyId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public static RepositoryPolicy Create(Guid repositoryId, Guid policyId)
    {
        return new RepositoryPolicy(repositoryId, policyId);
    }
}

public sealed class PackageBlob
{
    private PackageBlob()
    {
    }

    private PackageBlob(Guid packageVersionId, byte[] content, string sha256)
    {
        PackageVersionId = packageVersionId;
        Content = content;
        Sha256 = sha256;
        Size = content.LongLength;
        StoredAt = DateTimeOffset.UtcNow;
    }

    public Guid PackageVersionId { get; private set; }
    public byte[] Content { get; private set; } = [];
    public string Sha256 { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public DateTimeOffset StoredAt { get; private set; }

    public static PackageBlob Create(Guid packageVersionId, byte[] content, string sha256)
    {
        return new PackageBlob(packageVersionId, content, sha256);
    }
}

public sealed class SecurityScan
{
    private SecurityScan()
    {
    }

    private SecurityScan(Guid packageVersionId, string scannerVersion)
    {
        Id = Guid.CreateVersion7();
        PackageVersionId = packageVersionId;
        ScannerVersion = scannerVersion;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PackageVersionId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string ScannerVersion { get; private set; } = string.Empty;
    public ScanResult? Result { get; private set; }
    public int RiskScore { get; private set; }

    public static SecurityScan Start(Guid packageVersionId, string scannerVersion)
    {
        return new SecurityScan(packageVersionId, scannerVersion);
    }

    public void Complete(ScanResult result, int riskScore)
    {
        Result = result;
        RiskScore = Math.Max(0, riskScore);
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class SecurityFinding
{
    private SecurityFinding()
    {
    }

    private SecurityFinding(Guid scanId, string type, FindingSeverity severity, string title, string description,
        string source, string? externalReference, bool hardBlock, int riskScore)
    {
        Id = Guid.CreateVersion7();
        SecurityScanId = scanId;
        Type = type;
        Severity = severity;
        Title = title;
        Description = description;
        Source = source;
        ExternalReference = externalReference;
        IsHardBlock = hardBlock;
        RiskScore = Math.Max(0, riskScore);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid SecurityScanId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public FindingSeverity Severity { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string? ExternalReference { get; private set; }
    public bool IsHardBlock { get; private set; }
    public int RiskScore { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static SecurityFinding Create(Guid scanId, string type, FindingSeverity severity, string title,
        string description, string source, string? externalReference = null, bool hardBlock = false, int riskScore = 0)
    {
        return new SecurityFinding(scanId, type, severity, title, description, source, externalReference, hardBlock,
            riskScore);
    }
}

public sealed class PolicyRuleResult
{
    private PolicyRuleResult()
    {
    }

    private PolicyRuleResult(Guid packageVersionId, Guid? policyId, string rule, PolicyAction action, string reason,
        bool hardBlock)
    {
        Id = Guid.CreateVersion7();
        PackageVersionId = packageVersionId;
        PolicyId = policyId;
        Rule = rule;
        Action = action;
        Reason = reason;
        IsHardBlock = hardBlock;
        EvaluatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PackageVersionId { get; private set; }
    public Guid? PolicyId { get; private set; }
    public string Rule { get; private set; } = string.Empty;
    public PolicyAction Action { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public bool IsHardBlock { get; private set; }
    public DateTimeOffset EvaluatedAt { get; private set; }

    public static PolicyRuleResult Create(Guid packageVersionId, Guid? policyId, string rule, PolicyAction action,
        string reason, bool hardBlock = false)
    {
        return new PolicyRuleResult(packageVersionId, policyId, rule, action, reason, hardBlock);
    }
}

public sealed class PackageApproval
{
    private PackageApproval()
    {
    }

    private PackageApproval(Guid packageVersionId, ApprovalDecision decision, string reason, string createdBy,
        DateTimeOffset? expiresAt)
    {
        Id = Guid.CreateVersion7();
        PackageVersionId = packageVersionId;
        Decision = decision;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid PackageVersionId { get; private set; }
    public ApprovalDecision Decision { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static PackageApproval Create(Guid packageVersionId, ApprovalDecision decision, string reason,
        string createdBy, DateTimeOffset? expiresAt = null)
    {
        return new PackageApproval(packageVersionId, decision, Repository.Required(reason, nameof(reason), 2000),
            Repository.Required(createdBy, nameof(createdBy), 300), expiresAt);
    }

    public void MarkProcessed()
    {
        ProcessedAt ??= DateTimeOffset.UtcNow;
    }
}

public sealed class PackageApprovalRuleResult
{
    private PackageApprovalRuleResult()
    {
    }

    private PackageApprovalRuleResult(Guid packageApprovalId, Guid policyRuleResultId)
    {
        PackageApprovalId = packageApprovalId;
        PolicyRuleResultId = policyRuleResultId;
    }

    public Guid PackageApprovalId { get; private set; }
    public Guid PolicyRuleResultId { get; private set; }

    public static PackageApprovalRuleResult Create(Guid packageApprovalId, Guid policyRuleResultId)
    {
        return new PackageApprovalRuleResult(packageApprovalId, policyRuleResultId);
    }
}

public sealed class VulnerabilityCacheEntry
{
    private VulnerabilityCacheEntry()
    {
    }

    private VulnerabilityCacheEntry(string provider, PackageType packageType, string normalizedName, string version,
        string payloadJson, DateTimeOffset fetchedAt, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        Provider = Repository.Required(provider, nameof(provider), 100);
        PackageType = packageType;
        NormalizedName = Repository.Required(normalizedName, nameof(normalizedName), 300);
        Version = Repository.Required(version, nameof(version), 100);
        PayloadJson = Repository.Required(payloadJson, nameof(payloadJson), 1_000_000);
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public PackageType PackageType { get; private set; }
    public string NormalizedName { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset FetchedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public static VulnerabilityCacheEntry Create(string provider, PackageType packageType, string normalizedName,
        string version, string payloadJson, DateTimeOffset fetchedAt, DateTimeOffset expiresAt)
    {
        return new VulnerabilityCacheEntry(provider, packageType, normalizedName, version, payloadJson, fetchedAt,
            expiresAt);
    }

    public void Refresh(string payloadJson, DateTimeOffset fetchedAt, DateTimeOffset expiresAt)
    {
        PayloadJson = Repository.Required(payloadJson, nameof(payloadJson), 1_000_000);
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
        ConcurrencyToken = Guid.NewGuid();
    }
}

public sealed class BackgroundJobState
{
    private BackgroundJobState()
    {
    }

    private BackgroundJobState(string name)
    {
        Name = Repository.Required(name, nameof(name), 200);
        ConcurrencyToken = Guid.NewGuid();
    }

    public string Name { get; private set; } = string.Empty;
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public string? LastError { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public static BackgroundJobState Create(string name)
    {
        return new BackgroundJobState(name);
    }

    public bool TryAcquire(string owner, DateTimeOffset now, TimeSpan duration)
    {
        if (LeaseExpiresAt is not null && LeaseExpiresAt > now &&
            !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return false;
        LeaseOwner = Repository.Required(owner, nameof(owner), 200);
        LeaseExpiresAt = now.Add(duration);
        LastStartedAt = now;
        LastError = null;
        ConcurrencyToken = Guid.NewGuid();
        return true;
    }

    public void Complete(string owner, DateTimeOffset now, string? error = null)
    {
        if (!string.Equals(LeaseOwner, owner, StringComparison.Ordinal))
            throw new InvalidOperationException("The background job lease is owned by another worker.");
        LastCompletedAt = now;
        LastError = error;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        ConcurrencyToken = Guid.NewGuid();
    }
}

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    private AuditEvent(string actor, string action, string entityType, string entityId, string description,
        string dataJson)
    {
        Id = Guid.CreateVersion7();
        Timestamp = DateTimeOffset.UtcNow;
        Actor = actor;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Description = description;
        DataJson = dataJson;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string DataJson { get; private set; } = "{}";

    public static AuditEvent Create(string actor, string action, string entityType, string entityId, string description,
        string dataJson = "{}")
    {
        return new AuditEvent(actor, action, entityType, entityId, description, dataJson);
    }
}

public sealed class AccessToken
{
    private AccessToken()
    {
    }

    private AccessToken(string name, string tokenId, string verifier, string owner, string scopes,
        DateTimeOffset? expiresAt)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        TokenId = tokenId;
        Verifier = verifier;
        Owner = owner;
        Scopes = scopes;
        ExpiresAt = expiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
        Enabled = true;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TokenId { get; private set; } = string.Empty;
    public string Verifier { get; private set; } = string.Empty;
    public string Owner { get; private set; } = string.Empty;
    public string Scopes { get; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public bool Enabled { get; private set; }

    public static AccessToken Create(string name, string tokenId, string verifier, string owner,
        IEnumerable<string> scopes, DateTimeOffset? expiresAt)
    {
        return new AccessToken(Repository.Required(name, nameof(name), 200), tokenId, verifier, owner,
            string.Join(' ', scopes.Distinct(StringComparer.Ordinal)), expiresAt);
    }

    public bool MarkUsed(DateTimeOffset now)
    {
        if (LastUsedAt is not null && now - LastUsedAt < TimeSpan.FromMinutes(5)) return false;
        LastUsedAt = now;
        return true;
    }

    public void Revoke()
    {
        Enabled = false;
    }

    public bool IsActive(DateTimeOffset now)
    {
        return Enabled && (ExpiresAt is null || ExpiresAt > now);
    }

    public IReadOnlySet<string> GetScopes()
    {
        return Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }
}
