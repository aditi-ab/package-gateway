using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Application;

namespace PackageGateway.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GatewayInfrastructureOptions>()
            .Bind(configuration.GetSection(GatewayInfrastructureOptions.SectionName))
            .Validate(x => x.TokenPepper.Length >= 32, "Gateway:TokenPepper must contain at least 32 characters.")
            .ValidateOnStart();
        services.AddSingleton<IPackageOperationLock, InMemoryPackageOperationLock>();
        services.AddSingleton<IPackageAcquisitionCoordinator, PackageAcquisitionCoordinator>();
        services.AddScoped<IAccessTokenService, AccessTokenService>();
        services.AddHttpClient("gateway-upstream").AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
        });
        services.AddHttpClient<OsvVulnerabilityProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.osv.dev/");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler();
        services.AddSingleton<IVulnerabilityProvider>(sp => sp.GetRequiredService<OsvVulnerabilityProvider>());
        services.AddSingleton<JobHealthRegistry>();
        services.AddSingleton<DependencyHealthRegistry>();
        services.AddSingleton<IBackgroundJobLeaseProvider, DatabaseBackgroundJobLeaseProvider>();
        services.AddSingleton<IBackgroundJob, PendingPackageScanJob>();
        services.AddSingleton<IBackgroundJob, VulnerabilityRescanJob>();
        services.AddSingleton<IBackgroundJob, ExpiredApprovalJob>();
        services.AddSingleton<IBackgroundJob, CacheMaintenanceJob>();
        services.AddSingleton<IBackgroundJob, LegacyBlobMigrationJob>();
        services.AddSingleton<IBackgroundJob, UpstreamHealthJob>();
        services.AddSingleton<IBackgroundJob, OriginIntegrityMonitorJob>();
        services.AddHostedService<BackgroundJobRunner>();
        return services;
    }
}