using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Application;

namespace PackageGateway.Infrastructure;

public sealed class DatabaseBackgroundJobLeaseProvider(IServiceScopeFactory scopeFactory) : IBackgroundJobLeaseProvider
{
    private readonly string owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.CreateVersion7():N}";

    public async Task<IBackgroundJobLease?> TryAcquireAsync(string jobName, TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGatewayStore>();
        if (await store.TryAcquireJobLeaseAsync(jobName, owner, DateTimeOffset.UtcNow, duration, cancellationToken))
            return new Lease(scope, store, jobName, owner);
        await scope.DisposeAsync();
        return null;
    }

    private sealed class Lease(AsyncServiceScope scope, IGatewayStore store, string name, string owner)
        : IBackgroundJobLease
    {
        private bool finished;

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            await FinishAsync(null, cancellationToken);
        }

        public async Task FailAsync(string error, CancellationToken cancellationToken)
        {
            await FinishAsync(error, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (!finished)
                try
                {
                    await store.CompleteJobLeaseAsync(name, owner, DateTimeOffset.UtcNow,
                        "Job lease ended without a recorded outcome.", CancellationToken.None);
                }
                catch
                {
                }

            await scope.DisposeAsync();
        }

        private async Task FinishAsync(string? error, CancellationToken ct)
        {
            if (finished) return;
            await store.CompleteJobLeaseAsync(name, owner, DateTimeOffset.UtcNow, error, ct);
            finished = true;
        }
    }
}