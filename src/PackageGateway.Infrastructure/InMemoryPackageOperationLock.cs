using System.Collections.Concurrent;
using PackageGateway.Application;

namespace PackageGateway.Infrastructure;

public sealed class InMemoryPackageOperationLock : IPackageOperationLock
{
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var entry = entries.AddOrUpdate(key, _ => new Entry(), (_, existing) =>
        {
            Interlocked.Increment(ref existing.References);
            return existing;
        });
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry, false);
            throw;
        }
    }

    private void ReleaseReference(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore) entry.Semaphore.Release();
        if (Interlocked.Decrement(ref entry.References) == 0 &&
            entries.TryRemove(new KeyValuePair<string, Entry>(key, entry))) entry.Semaphore.Dispose();
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References = 1;
    }

    private sealed class Releaser(InMemoryPackageOperationLock owner, string key, Entry entry) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.ReleaseReference(key, entry, true);
            return ValueTask.CompletedTask;
        }
    }
}