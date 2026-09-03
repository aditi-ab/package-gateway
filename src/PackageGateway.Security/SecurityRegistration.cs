using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Application;

namespace PackageGateway.Security;

public static class SecurityRegistration
{
    public static IServiceCollection AddGatewaySecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new SecurityOptions();
        configuration.GetSection(SecurityOptions.SectionName).Bind(options);
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName))
            .Validate(
                x => x.MaximumPackageBytes > 0 && x.MaximumExpandedBytes >= x.MaximumPackageBytes &&
                     x.MaximumFileBytes > 0 && x.MaximumFileCount > 0, "Security archive limits are invalid.")
            .Validate(
                x => x.MaximumCompressionRatio >= 1 && x.ScanTimeout > TimeSpan.Zero &&
                     x.InitialRequestWait > TimeSpan.Zero && x.VulnerabilityCacheMaximumAge > TimeSpan.Zero,
                "Security time and ratio limits are invalid.")
            .Validate(x => x.BlockedSha256Digests.All(d => d.Length == 64 && d.All(Uri.IsHexDigit)),
                "Every blocked SHA-256 digest must contain exactly 64 hexadecimal characters.").ValidateOnStart();
        services.AddSingleton(options);
        services.AddSingleton<IMalwareScanner, KnownDigestMalwareScanner>();
        services.AddSingleton<IPackageScanner, ArchivePackageScanner>();
        services.AddScoped<IPackagePolicyEvaluator, PolicyEvaluator>();
        return services;
    }
}