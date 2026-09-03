using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PackageGateway.Application;
using PackageGateway.Domain;
using PackageGateway.Security;

namespace PackageGateway.Infrastructure;

public sealed class PackageAcquisitionCoordinator(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IPackageOperationLock operationLock,
    SecurityOptions options,
    ILogger<PackageAcquisitionCoordinator> logger) : IPackageAcquisitionCoordinator
{
    private readonly ConcurrentDictionary<string, Lazy<Task<Outcome>>> operations = new(StringComparer.Ordinal);

    public async Task<ArtifactDelivery> GetOrAcquireAsync(ArtifactRequest request, CancellationToken cancellationToken)
    {
        GatewayDiagnostics.AcquisitionRequests.Add(1,
            new KeyValuePair<string, object?>("package.type", request.PackageType.ToString()));
        var key =
            $"{request.RepositoryId:N}:{request.PackageType}:{PackageIdentity.Normalize(request.PackageName, request.PackageType)}:{request.Version.ToLowerInvariant()}";
        var operation = operations.GetOrAdd(key, _ => Start(key, request));
        Outcome outcome;
        try
        {
            outcome = await operation.Value.WaitAsync(options.InitialRequestWait, cancellationToken);
        }
        catch (TimeoutException)
        {
            GatewayDiagnostics.AcquisitionOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "pending"));
            return ArtifactDelivery.Pending("Security evaluation is still running.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Package acquisition failed for repository {RepositoryId}", request.RepositoryId);
            GatewayDiagnostics.AcquisitionOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            return ArtifactDelivery.Failed("Package acquisition failed closed.");
        }

        GatewayDiagnostics.AcquisitionOutcomes.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome.Status.ToString().ToLowerInvariant()));
        if (outcome.Status != ArtifactDeliveryStatus.Approved)
            return new ArtifactDelivery(outcome.Status, Message: outcome.Message);
        await using var scope = scopeFactory.CreateAsyncScope();
        var blobs = scope.ServiceProvider.GetRequiredService<IPackageBlobStore>();
        var content = await blobs.OpenReadAsync(outcome.PackageVersionId, cancellationToken);
        return content is null
            ? ArtifactDelivery.Failed("Approved artifact blob is unavailable.")
            : new ArtifactDelivery(ArtifactDeliveryStatus.Approved, content, outcome.ContentType, outcome.Length,
                outcome.Sha256);
    }

    public async Task VerifyOriginAsync(Guid packageVersionId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        var version = await store.FindPackageVersionByIdAsync(packageVersionId, cancellationToken);
        if (version?.Status != PackageVersionStatus.Approved || version.Sha256 is null) return;
        var package = await store.FindPackageAsync(version.PackageId, cancellationToken);
        var upstream = await store.FindUpstreamAsync(version.UpstreamId, cancellationToken);
        if (package is null || upstream is null) return;
        var client = scope.ServiceProvider.GetServices<IUpstreamClient>()
            .SingleOrDefault(x => x.PackageType == package.PackageType);
        if (client is null)
            throw new InvalidOperationException($"No upstream client is registered for {package.PackageType}.");
        var resolved = await client.ResolveExactAsync(upstream, package.Name, version.Version, cancellationToken);
        if (resolved is null)
        {
            await store.AddAuditAsync(
                AuditEvent.Create("system", "PackageOriginUnavailable", nameof(PackageVersion), version.Id.ToString(),
                    "The pinned origin no longer advertises the package version; approved local bytes were retained."),
                cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var remote = await DownloadArtifactAsync(resolved.ArtifactUri, upstream.Trusted, cancellationToken);
        var remoteHash = await ComputeSha256Async(remote, cancellationToken);
        var integrityMismatch = resolved.ExpectedIntegrity is { Length: > 0 } integrity &&
                                !await MatchesIntegrityAsync(remote, integrity, cancellationToken);
        if (!StringComparer.OrdinalIgnoreCase.Equals(remoteHash, version.Sha256) || integrityMismatch)
            await RecordIntegrityConflictAsync(store, version,
                "The pinned upstream now serves bytes or integrity metadata that conflict with the approved immutable artifact.");
    }

    private Lazy<Task<Outcome>> Start(string key, ArtifactRequest request)
    {
        return new Lazy<Task<Outcome>>(() =>
        {
            var task = RunAsync(key, request);
            _ = task.ContinueWith(completed => operations.TryRemove(key, out var removed), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private async Task<Outcome> RunAsync(string operationKey, ArtifactRequest request)
    {
        using var activity = GatewayDiagnostics.Activities.StartActivity("package.acquire");
        activity?.SetTag("package.type", request.PackageType.ToString());
        activity?.SetTag("package.repository_id", request.RepositoryId);
        await using var operationLease = await operationLock.AcquireAsync(operationKey, CancellationToken.None);
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        var blobs = scope.ServiceProvider.GetRequiredService<IPackageBlobStore>();
        var repository = await store.FindRepositoryAsync(request.RepositoryId, CancellationToken.None);
        if (repository is null || !repository.Enabled) return Outcome.NotFound("Repository not found or disabled.");
        var upstream = await store.FindUpstreamAsync(request.UpstreamId, CancellationToken.None);
        if (upstream is null || upstream.RepositoryId != repository.Id || !upstream.Enabled ||
            upstream.PackageType != request.PackageType) return Outcome.NotFound("Upstream not found or disabled.");
        if (!await IsSafeArtifactUriAsync(request.ArtifactUri, upstream.Trusted, CancellationToken.None))
            return Outcome.Denied("Artifact URL failed outbound security validation.");
        var normalized = PackageIdentity.Normalize(request.PackageName, request.PackageType);
        var existing = await store.FindPackageVersionAsync(repository.Id, request.PackageType, normalized,
            request.Version, CancellationToken.None);
        if (existing is not null)
        {
            if (existing.Value.Version.UpstreamId != upstream.Id)
            {
                await RecordOriginConflictAsync(store, existing.Value.Version, upstream.Id);
                return Outcome.Denied("Package version is pinned to a different upstream.");
            }

            if (existing.Value.Version.CanBeDelivered)
            {
                if (await ConflictsWithApprovedBytesAsync(existing.Value.Version, request, blobs,
                        CancellationToken.None))
                {
                    await RecordIntegrityConflictAsync(store, existing.Value.Version,
                        "Upstream metadata no longer matches the approved immutable artifact.");
                    return Outcome.Denied("Upstream integrity metadata conflicts with the approved artifact.");
                }

                return Outcome.Approved(existing.Value.Version, ContentType(request.PackageType));
            }

            if (existing.Value.Version.Status is PackageVersionStatus.Blocked or PackageVersionStatus.Quarantined
                or PackageVersionStatus.ManualReview)
                return Outcome.Denied($"Artifact status is {existing.Value.Version.Status}.");
        }

        var pair = await store.GetOrCreatePackageVersionAsync(repository.Id, request.PackageType, request.PackageName,
            request.Version, upstream.Id, request.ArtifactUri.AbsoluteUri, request.PublishedAt, request.ExpectedSha256,
            request.ExpectedIntegrity, CancellationToken.None);
        var package = pair.Package;
        var version = pair.Version;
        if (version.Status == PackageVersionStatus.Scanning) version.QueueRescan();
        version.BeginScan();
        if (existing is null)
            await store.AddAuditAsync(
                AuditEvent.Create("system", "PackageFirstObserved", nameof(PackageVersion), version.Id.ToString(),
                    $"First observed {package.Name} {version.Version} from upstream {upstream.Id}."),
                CancellationToken.None);
        await store.AddAuditAsync(
            AuditEvent.Create("system", "PackageScanStarted", nameof(PackageVersion), version.Id.ToString(),
                $"Scanning {package.Name} {version.Version}."), CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);
        try
        {
            using var scanCts = new CancellationTokenSource(options.ScanTimeout);
            var scanToken = scanCts.Token;
            await using var artifact = await GetArtifactAsync(version, blobs, upstream.Trusted, scanToken);
            var actualSha256 = await ComputeSha256Async(artifact, scanToken);
            artifact.Position = 0;
            var extraFindings = new List<ScanFinding>();
            var expectedSha256 = request.ExpectedSha256 ?? version.ExpectedSha256;
            var expectedIntegrity = request.ExpectedIntegrity ?? version.ExpectedIntegrity;
            if (expectedSha256 is { Length: > 0 } expected &&
                !StringComparer.OrdinalIgnoreCase.Equals(expected, actualSha256))
                extraFindings.Add(new ScanFinding("Integrity", FindingSeverity.Critical, "Artifact digest mismatch",
                    "Downloaded bytes do not match the expected digest.", "Acquisition", IsHardBlock: true,
                    RiskScore: 100));
            if (expectedIntegrity is { Length: > 0 } integrity &&
                !await MatchesIntegrityAsync(artifact, integrity, scanToken))
                extraFindings.Add(new ScanFinding("Integrity", FindingSeverity.Critical, "Artifact integrity mismatch",
                    "Downloaded bytes do not match the package registry integrity value.", "Acquisition",
                    IsHardBlock: true, RiskScore: 100));
            if (version.Sha256 is { } pinned && !StringComparer.OrdinalIgnoreCase.Equals(pinned, actualSha256))
                extraFindings.Add(new ScanFinding("Integrity", FindingSeverity.Critical, "Immutable artifact changed",
                    "Upstream returned different bytes for a pinned package version.", "Acquisition", IsHardBlock: true,
                    RiskScore: 100));
            if (version.Sha256 is null)
            {
                await blobs.StoreAsync(version.Id, artifact, actualSha256, options.MaximumPackageBytes, scanToken);
                version.SetArtifact(actualSha256, artifact.Length);
                await store.AddAuditAsync(
                    AuditEvent.Create("system", "PackageDownloaded", nameof(PackageVersion), version.Id.ToString(),
                        $"Stored immutable artifact SHA-256 {actualSha256}."), scanToken);
                await store.SaveChangesAsync(scanToken);
            }

            artifact.Position = 0;
            var scanner = scope.ServiceProvider.GetServices<IPackageScanner>()
                .First(x => x.Supports(package.PackageType));
            PackageInspectionResult inspection;
            var scanResult = ScanResult.Succeeded;
            var scanStarted = Stopwatch.GetTimestamp();
            try
            {
                inspection = await scanner.ScanAsync(package.PackageType, artifact, scanToken);
            }
            catch (OperationCanceledException) when (scanCts.IsCancellationRequested)
            {
                scanResult = ScanResult.TimedOut;
                inspection = FailureInspection("Security scan timed out.");
            }
            catch (Exception ex)
            {
                scanResult = ScanResult.Failed;
                logger.LogError(ex, "Scanner failed for package version {PackageVersionId}", version.Id);
                inspection = FailureInspection("A security scanner failed unexpectedly.");
            }
            finally
            {
                GatewayDiagnostics.ScanDuration.Record(Stopwatch.GetElapsedTime(scanStarted).TotalSeconds,
                    new KeyValuePair<string, object?>("package.type", package.PackageType.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(inspection.PackageName))
                try
                {
                    package.UpdateDisplayName(inspection.PackageName);
                }
                catch (InvalidOperationException)
                {
                    extraFindings.Add(new ScanFinding("Integrity", FindingSeverity.Critical,
                        "Package identity mismatch",
                        "The package manifest name does not match the requested package identity.", "Acquisition",
                        IsHardBlock: true, RiskScore: 100));
                }

            inspection = inspection with
            {
                Findings = inspection.Findings.Concat(extraFindings).ToArray(),
                RiskScore = inspection.RiskScore + extraFindings.Sum(x => x.RiskScore)
            };
            var evaluationToken = CancellationToken.None;
            var vulnerabilities = new List<Vulnerability>();
            foreach (var provider in scope.ServiceProvider.GetServices<IVulnerabilityProvider>())
                try
                {
                    vulnerabilities.AddRange(await provider.GetVulnerabilitiesAsync(package.PackageType, package.Name,
                        version.Version, evaluationToken));
                }
                catch (VulnerabilityProviderUnavailableException ex)
                {
                    logger.LogWarning(ex, "Vulnerability provider unavailable for {Package} {Version}", package.Name,
                        version.Version);
                    inspection = inspection with
                    {
                        Findings = inspection.Findings.Append(new ScanFinding("Vulnerability", FindingSeverity.High,
                            "Vulnerability data unavailable", ex.Message, provider.Name, RiskScore: 40)).ToArray(),
                        RiskScore = inspection.RiskScore + 40
                    };
                }

            inspection = inspection with
            {
                RiskScore = inspection.RiskScore + vulnerabilities.Sum(x => VulnerabilityRisk(x.Severity))
            };
            var policies = await store.GetAssignedPoliciesAsync(repository.Id, evaluationToken);
            var evaluator = scope.ServiceProvider.GetRequiredService<IPackagePolicyEvaluator>();
            var evaluation = await evaluator.EvaluateAsync(
                new PolicyEvaluationContext(package, version, inspection, vulnerabilities, policies,
                    DateTimeOffset.UtcNow), evaluationToken);
            GatewayDiagnostics.PolicyOutcomes.Add(1,
                new KeyValuePair<string, object?>("action", evaluation.FinalAction.ToString()));
            var status = evaluation.FinalAction switch
            {
                PolicyAction.Allow or PolicyAction.Warn => PackageVersionStatus.Approved,
                PolicyAction.ManualReview => PackageVersionStatus.ManualReview,
                PolicyAction.Quarantine => PackageVersionStatus.Quarantined, _ => PackageVersionStatus.Blocked
            };
            var scan = SecurityScan.Start(version.Id,
                typeof(PackageAcquisitionCoordinator).Assembly.GetName().Version?.ToString() ?? "1.0.0");
            var findings = inspection.Findings.Select(x => SecurityFinding.Create(scan.Id, x.Type, x.Severity, x.Title,
                    x.Description, x.Source, x.ExternalReference, x.IsHardBlock, x.RiskScore))
                .Concat(vulnerabilities.Select(x => SecurityFinding.Create(scan.Id, "Vulnerability", x.Severity,
                    x.ExternalId, x.Summary, "OSV.dev", x.Url, riskScore: VulnerabilityRisk(x.Severity)))).ToArray();
            var ruleResults = evaluation.Rules.Select(x =>
                PolicyRuleResult.Create(version.Id, x.PolicyId, x.Rule, x.Action, x.Reason, x.IsHardBlock)).ToArray();
            scan.Complete(scanResult, evaluation.RiskScore);
            version.CompleteScan(status, evaluation.RiskScore, evaluation.HasHardBlock, inspection.SignatureStatus,
                inspection.HasInstallScripts, inspection.License);
            await store.AddScanAsync(scan, findings, ruleResults, evaluationToken);
            await store.AddAuditAsync(
                AuditEvent.Create("system", $"Package{status}", nameof(PackageVersion), version.Id.ToString(),
                    $"Security evaluation completed with {status}."), evaluationToken);
            await store.SaveChangesAsync(evaluationToken);
            return status == PackageVersionStatus.Approved
                ? Outcome.Approved(version, ContentType(package.PackageType))
                : Outcome.Denied($"Artifact status is {status}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Acquisition pipeline failed closed for {PackageVersionId}", version.Id);
            if (version.Status == PackageVersionStatus.Scanning) version.QueueRescan();
            await store.AddAuditAsync(
                AuditEvent.Create("system", "PackageScanFailed", nameof(PackageVersion), version.Id.ToString(),
                    "Acquisition failed closed; package returned to pending."), CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
            return Outcome.Failed("Acquisition or security evaluation failed closed.");
        }
    }

    private async Task<Stream> GetArtifactAsync(PackageVersion version, IPackageBlobStore blobs, bool trustedUpstream,
        CancellationToken ct)
    {
        var cached = await blobs.OpenReadAsync(version.Id, ct);
        if (cached is not null) return cached;
        return await DownloadArtifactAsync(new Uri(version.ArtifactUrl), trustedUpstream, ct);
    }

    private async Task<Stream> DownloadArtifactAsync(Uri uri, bool trustedUpstream, CancellationToken ct)
    {
        if (!await IsSafeArtifactUriAsync(uri, trustedUpstream, ct))
            throw new InvalidOperationException("Artifact URL failed outbound security validation.");
        using var response = await httpClientFactory.CreateClient("gateway-upstream")
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > options.MaximumPackageBytes)
            throw new InvalidDataException("Artifact exceeds the configured package-size limit.");
        var tempPath = Path.Combine(Path.GetTempPath(), $"package-gateway-{Guid.CreateVersion7():N}.tmp");
        var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > options.MaximumPackageBytes)
                    throw new InvalidDataException("Artifact exceeds the configured package-size limit.");
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken ct)
    {
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<bool> MatchesIntegrityAsync(Stream stream, string integrity, CancellationToken ct)
    {
        var first = integrity.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return false;
        var parts = first.Split('-', 2);
        if (parts.Length != 2) return false;
        stream.Position = 0;
        var actual = parts[0].ToLowerInvariant() switch
        {
            "sha512" => await SHA512.HashDataAsync(stream, ct),
            "sha256" => await SHA256.HashDataAsync(stream, ct),
            "sha1" => await SHA1.HashDataAsync(stream, ct),
            _ => []
        };
        stream.Position = 0;
        if (actual.Length == 0) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(parts[1]));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<bool> IsSafeArtifactUriAsync(Uri uri, bool trusted, CancellationToken ct)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (trusted) return true;
        if (IPAddress.TryParse(uri.Host, out var literal)) return IsPublic(literal);
        try
        {
            return (await Dns.GetHostAddressesAsync(uri.Host, ct)).All(IsPublic);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 16)
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast &&
                   (bytes[0] & 0xfe) != 0xfc;
        return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 ||
                 (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                 (bytes[0] == 169 && bytes[1] == 254) ||
                 (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                 (bytes[0] == 192 && bytes[1] is 0 or 168) ||
                 (bytes[0] == 198 && bytes[1] is 18 or 19 or 51) ||
                 (bytes[0] == 203 && bytes[1] == 0));
    }

    private static async Task RecordOriginConflictAsync(IGatewayStore store, PackageVersion version,
        Guid requestedUpstream)
    {
        var description =
            $"Pinned upstream {version.UpstreamId} conflicts with requested upstream {requestedUpstream}.";
        await RecordIntegrityConflictAsync(store, version, description, "PackageOriginConflict");
    }

    private static async Task<bool> ConflictsWithApprovedBytesAsync(PackageVersion version, ArtifactRequest request,
        IPackageBlobStore blobs, CancellationToken ct)
    {
        if (request.ExpectedSha256 is { Length: > 0 } expectedSha256 &&
            !StringComparer.OrdinalIgnoreCase.Equals(expectedSha256, version.Sha256)) return true;
        if (request.ExpectedIntegrity is not { Length: > 0 } expectedIntegrity) return false;
        await using var content = await blobs.OpenReadAsync(version.Id, ct);
        return content is null || !await MatchesIntegrityAsync(content, expectedIntegrity, ct);
    }

    private static async Task RecordIntegrityConflictAsync(IGatewayStore store, PackageVersion version,
        string description, string? additionalAuditAction = null)
    {
        version.ApplyHardBlock();
        var scan = SecurityScan.Start(version.Id, "integrity-monitor");
        scan.Complete(ScanResult.Failed, 100);
        var finding = SecurityFinding.Create(scan.Id, "Integrity", FindingSeverity.Critical,
            "Immutable artifact conflict", description, "Acquisition", hardBlock: true, riskScore: 100);
        var rule = PolicyRuleResult.Create(version.Id, null, "IntegrityGuard", PolicyAction.Block, description, true);
        await store.AddScanAsync(scan, [finding], [rule], CancellationToken.None);
        await store.AddAuditAsync(
            AuditEvent.Create("system", "PackageIntegrityConflict", nameof(PackageVersion), version.Id.ToString(),
                description), CancellationToken.None);
        if (additionalAuditAction is not null)
            await store.AddAuditAsync(
                AuditEvent.Create("system", additionalAuditAction, nameof(PackageVersion), version.Id.ToString(),
                    description), CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);
    }

    private static PackageInspectionResult FailureInspection(string message)
    {
        return new PackageInspectionResult(
        [
            new ScanFinding("Scanner", FindingSeverity.High, "Security evaluation unavailable", message, "Gateway",
                RiskScore: 40)
        ], 40, false, SignatureStatus.Unknown, null);
    }

    private static int VulnerabilityRisk(FindingSeverity severity)
    {
        return severity switch
        {
            FindingSeverity.Critical => 100, FindingSeverity.High => 70, FindingSeverity.Medium => 30,
            FindingSeverity.Low => 5, _ => 0
        };
    }

    private static string ContentType(PackageType type)
    {
        return type == PackageType.NuGet ? "application/octet-stream" : "application/gzip";
    }

    private sealed record Outcome(
        ArtifactDeliveryStatus Status,
        Guid PackageVersionId,
        string? ContentType,
        long? Length,
        string? Sha256,
        string? Message)
    {
        public static Outcome Approved(PackageVersion version, string contentType)
        {
            return new Outcome(ArtifactDeliveryStatus.Approved, version.Id, contentType, version.Size, version.Sha256,
                null);
        }

        public static Outcome Denied(string message)
        {
            return new Outcome(ArtifactDeliveryStatus.Denied, Guid.Empty, null, null, null, message);
        }

        public static Outcome NotFound(string message)
        {
            return new Outcome(ArtifactDeliveryStatus.NotFound, Guid.Empty, null, null, null, message);
        }

        public static Outcome Failed(string message)
        {
            return new Outcome(ArtifactDeliveryStatus.Failed, Guid.Empty, null, null, null, message);
        }
    }
}