namespace PackageGateway.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public long MaximumPackageBytes { get; set; } = 250L * 1024 * 1024;
    public long MaximumExpandedBytes { get; set; } = 1024L * 1024 * 1024;
    public long MaximumFileBytes { get; set; } = 100L * 1024 * 1024;
    public int MaximumFileCount { get; set; } = 10_000;
    public double MaximumCompressionRatio { get; set; } = 200;
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan InitialRequestWait { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan VulnerabilityCacheMaximumAge { get; set; } = TimeSpan.FromHours(24);
    public string[] BlockedSha256Digests { get; set; } = [];
}