using Microsoft.Extensions.Diagnostics.HealthChecks;
using PackageGateway.Application;

namespace PackageGateway.Api;

public sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        try
        {
            if (!await store.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Database is unavailable.");
            if (await store.HasPendingMigrationsAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Database has pending migrations.");
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database readiness check failed.", ex);
        }
    }
}