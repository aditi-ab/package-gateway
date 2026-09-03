using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using PackageGateway.Application;
using PackageGateway.Domain;
using HashAlgorithmName = NuGet.Common.HashAlgorithmName;

namespace PackageGateway.Security;

public sealed class ArchivePackageScanner(SecurityOptions options, IEnumerable<IMalwareScanner> malwareScanners)
    : IPackageScanner
{
    public bool Supports(PackageType packageType)
    {
        return packageType is PackageType.NuGet or PackageType.Npm;
    }

    public async Task<PackageInspectionResult> ScanAsync(PackageType packageType, Stream artifact,
        CancellationToken cancellationToken)
    {
        if (!artifact.CanSeek) throw new ArgumentException("The scanner requires a seekable stream.", nameof(artifact));
        if (artifact.Length > options.MaximumPackageBytes)
            return HardLimit("Artifact exceeds the compressed-size limit.");
        var findings = new List<ScanFinding>();
        string? packageName = null;
        string? license = null;
        var hasInstallScripts = false;
        var signature = SignatureStatus.Unknown;
        try
        {
            if (packageType == PackageType.NuGet)
                (packageName, license, signature) = await InspectNuGetAsync(artifact, findings, cancellationToken);
            else
                (packageName, license, hasInstallScripts) =
                    await InspectNpmAsync(artifact, findings, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException or JsonException)
        {
            findings.Add(new ScanFinding("Archive", FindingSeverity.Critical, "Malformed or unsafe archive", ex.Message,
                "ArchiveScanner", IsHardBlock: true, RiskScore: 100));
        }

        foreach (var scanner in malwareScanners)
        {
            artifact.Position = 0;
            findings.AddRange(await scanner.ScanAsync(packageType, artifact, cancellationToken));
        }

        return new PackageInspectionResult(findings, findings.Sum(x => x.RiskScore), hasInstallScripts, signature,
            license, packageName);
    }

    private async Task<(string? PackageName, string? License, SignatureStatus Signature)> InspectNuGetAsync(
        Stream artifact, List<ScanFinding> findings, CancellationToken ct)
    {
        artifact.Position = 0;
        long expanded = 0;
        var count = 0;
        string? packageName = null;
        string? license = null;
        var signature = SignatureStatus.Unsigned;
        var hasSignature = false;
        using (var archive = new ZipArchive(artifact, ZipArchiveMode.Read, true))
        {
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                ValidateEntry(entry.FullName, entry.Length, ref expanded, ref count);
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException($"Archive entry '{entry.FullName}' is a symbolic link.");
                if (entry.CompressedLength > 0 &&
                    entry.Length / (double)entry.CompressedLength > options.MaximumCompressionRatio)
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' exceeds the compression-ratio limit.");
                if (entry.FullName.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase)) hasSignature = true;
                if (entry.FullName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                    findings.Add(new ScanFinding("Script", FindingSeverity.Medium, "PowerShell content",
                        $"Package contains {entry.FullName}.", "ArchiveScanner", RiskScore: 30));
                if (entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.Replace('\\', '/').Contains('/'))
                {
                    await using var stream = entry.Open();
                    using var reader = XmlReader.Create(stream,
                        new XmlReaderSettings
                        {
                            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                            MaxCharactersInDocument = 2_000_000
                        });
                    while (await reader.ReadAsync())
                    {
                        if (reader.NodeType != XmlNodeType.Element) continue;
                        if (reader.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))
                            packageName = (await reader.ReadElementContentAsStringAsync()).Trim();
                        else if (reader.LocalName.Equals("license", StringComparison.OrdinalIgnoreCase))
                            license = (await reader.ReadElementContentAsStringAsync()).Trim();
                    }
                }
            }
        }

        if (artifact.Length > 0 && expanded / (double)artifact.Length > options.MaximumCompressionRatio)
            throw new InvalidDataException("Archive exceeds the overall compression-ratio limit.");
        if (hasSignature) signature = await VerifyNuGetSignatureAsync(artifact, ct);
        if (signature == SignatureStatus.Unsigned)
            findings.Add(new ScanFinding("Signature", FindingSeverity.Low, "Unsigned NuGet package",
                "The package does not contain a NuGet signature.", "ArchiveScanner", RiskScore: 0));
        else if (signature == SignatureStatus.Invalid)
            findings.Add(new ScanFinding("Signature", FindingSeverity.Critical, "Invalid NuGet signature",
                "NuGet signature trust or package integrity verification failed.", "NuGet.Packaging", IsHardBlock: true,
                RiskScore: 100));
        return (packageName, license, signature);
    }

    private static async Task<SignatureStatus> VerifyNuGetSignatureAsync(Stream artifact, CancellationToken ct)
    {
        try
        {
            X509TrustStore.InitializeForDotNetSdk(NullLogger.Instance);
            artifact.Position = 0;
            using var reader = new PackageArchiveReader(artifact, true);
            var providers = new ISignatureVerificationProvider[]
            {
                new IntegrityVerificationProvider(),
                new SignatureTrustAndValidityVerificationProvider(
                    Array.Empty<KeyValuePair<string, HashAlgorithmName>>())
            };
            var verifier = new PackageSignatureVerifier(providers);
            var settings =
                SignedPackageVerifierSettings.GetVerifyCommandDefaultPolicy(EnvironmentVariableWrapper.Instance);
            var result = await verifier.VerifySignaturesAsync(reader, settings, ct, Guid.NewGuid());
            artifact.Position = 0;
            return result.IsSigned && result.IsValid ? SignatureStatus.Valid : SignatureStatus.Invalid;
        }
        catch (Exception ex) when (ex is SignatureException or InvalidDataException or CryptographicException)
        {
            artifact.Position = 0;
            return SignatureStatus.Invalid;
        }
    }

    private async Task<(string? PackageName, string? License, bool HasScripts)> InspectNpmAsync(Stream artifact,
        List<ScanFinding> findings, CancellationToken ct)
    {
        artifact.Position = 0;
        await using var gzip = new GZipStream(artifact, CompressionMode.Decompress, true);
        using var tar = new TarReader(gzip, true);
        long expanded = 0;
        var count = 0;
        string? packageName = null;
        string? license = null;
        var hasScripts = false;
        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync(false, ct)) is not null)
        {
            ValidateEntry(entry.Name, entry.Length, ref expanded, ref count);
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                throw new InvalidDataException($"Archive entry '{entry.Name}' is a link.");
            var metadataPath = entry.Name.Replace('\\', '/').TrimStart('.', '/');
            if (!metadataPath.Equals("package/package.json", StringComparison.OrdinalIgnoreCase) ||
                entry.DataStream is null) continue;
            using var limited = new MemoryStream();
            await CopyBoundedAsync(entry.DataStream, limited, Math.Min(options.MaximumFileBytes, 2 * 1024 * 1024), ct);
            limited.Position = 0;
            using var json = await JsonDocument.ParseAsync(limited, cancellationToken: ct);
            var root = json.RootElement;
            if (root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                packageName = nameProperty.GetString();
            if (root.TryGetProperty("license", out var licenseProperty) &&
                licenseProperty.ValueKind == JsonValueKind.String) license = licenseProperty.GetString();
            if (root.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
                foreach (var name in new[] { "preinstall", "install", "postinstall", "prepare" })
                {
                    if (!scripts.TryGetProperty(name, out var value) ||
                        value.ValueKind != JsonValueKind.String) continue;
                    hasScripts = true;
                    var script = value.GetString() ?? string.Empty;
                    findings.Add(new ScanFinding("InstallScript", FindingSeverity.Medium, $"npm {name} script",
                        "Package defines an install-time lifecycle script.", "ArchiveScanner", RiskScore: 20));
                    AddScriptSignals(script, findings);
                }
        }

        if (artifact.Length > 0 && expanded / (double)artifact.Length > options.MaximumCompressionRatio)
            throw new InvalidDataException("Archive exceeds the overall compression-ratio limit.");
        return (packageName, license, hasScripts);
    }

    private static void AddScriptSignals(string script, List<ScanFinding> findings)
    {
        var signals = new (string Pattern, string Title, int Score)[]
        {
            ("powershell", "PowerShell execution", 30), ("child_process", "Child process execution", 30),
            ("curl ", "Network download", 30),
            ("wget ", "Network download", 30), ("cmd.exe", "Command shell execution", 30),
            ("bash ", "Shell execution", 30),
            ("frombase64", "Base64 decoding", 20), ("process.env", "Environment-variable access", 15)
        };
        foreach (var signal in signals.Where(x => script.Contains(x.Pattern, StringComparison.OrdinalIgnoreCase)))
            findings.Add(new ScanFinding("MalwareIndicator", FindingSeverity.Medium, signal.Title,
                "A lifecycle script contains a potentially risky behavior; this is a heuristic, not a malware verdict.",
                "HeuristicScanner", RiskScore: signal.Score));
    }

    private void ValidateEntry(string name, long length, ref long expanded, ref int count)
    {
        var normalized = name.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') ||
            normalized.Split('/').Any(x => x == "..") ||
            Path.IsPathRooted(name)) throw new InvalidDataException($"Unsafe archive path '{name}'.");
        if (length > options.MaximumFileBytes)
            throw new InvalidDataException($"Archive entry '{name}' exceeds the individual-file limit.");
        checked
        {
            expanded += length;
            count++;
        }

        if (expanded > options.MaximumExpandedBytes)
            throw new InvalidDataException("Archive exceeds the expanded-size limit.");
        if (count > options.MaximumFileCount) throw new InvalidDataException("Archive exceeds the file-count limit.");
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximum, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maximum) throw new InvalidDataException("Metadata file exceeds its limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static PackageInspectionResult HardLimit(string message)
    {
        return new PackageInspectionResult(
        [
            new ScanFinding("Archive", FindingSeverity.Critical, "Unsafe artifact", message, "ArchiveScanner",
                IsHardBlock: true,
                RiskScore: 100)
        ], 100, false, SignatureStatus.Unknown, null);
    }
}

public sealed class NoOpMalwareScanner : IMalwareScanner
{
    public Task<IReadOnlyList<ScanFinding>> ScanAsync(PackageType packageType, Stream artifact,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ScanFinding>>([]);
    }
}

public sealed class KnownDigestMalwareScanner(SecurityOptions options) : IMalwareScanner
{
    private readonly HashSet<string> blockedDigests = options.BlockedSha256Digests
        .Where(x => x.Length == 64 && x.All(Uri.IsHexDigit))
        .Select(x => x.ToLowerInvariant())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ScanFinding>> ScanAsync(PackageType packageType, Stream artifact,
        CancellationToken cancellationToken)
    {
        if (blockedDigests.Count == 0) return [];
        artifact.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(artifact, cancellationToken)).ToLowerInvariant();
        return blockedDigests.Contains(digest)
            ?
            [
                new ScanFinding("Malware", FindingSeverity.Critical, "Confirmed malware digest",
                    "The artifact digest matches the configured malware indicator set.", "KnownDigestScanner",
                    IsHardBlock: true, RiskScore: 100)
            ]
            : [];
    }
}
