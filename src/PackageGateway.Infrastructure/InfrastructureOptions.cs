namespace PackageGateway.Infrastructure;

public sealed class GatewayInfrastructureOptions
{
    public const string SectionName = "Gateway";
    public string TokenPepper { get; set; } = string.Empty;
    public TimeSpan VulnerabilityRescanInterval { get; set; } = TimeSpan.FromHours(12);
    public TimeSpan BackgroundJobLeaseDuration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan OriginIntegrityInterval { get; set; } = TimeSpan.FromHours(12);
}