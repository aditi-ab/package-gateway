using System.Collections.Concurrent;
using PackageGateway.Application;

namespace PackageGateway.IntegrationTests;

internal sealed class TestPackageBlobStore : IPackageBlobStore
{
    private readonly ConcurrentDictionary<Guid, byte[]> content = new();

    public Task<Stream?> OpenReadAsync(Guid packageVersionId, CancellationToken cancellationToken)
    {
        return Task.FromResult<Stream?>(content.TryGetValue(packageVersionId, out var value)
            ? new MemoryStream(value, false)
            : null);
    }

    public async Task StoreAsync(Guid packageVersionId, Stream source, string sha256, long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maximumBytes) throw new InvalidDataException("Artifact exceeds configured maximum size.");
        content.TryAdd(packageVersionId, buffer.ToArray());
    }

    public Task<bool> DeleteUnapprovedAsync(Guid packageVersionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(content.TryRemove(packageVersionId, out _));
    }

    public Task DeleteAsync(Guid packageVersionId, string? sha256, CancellationToken cancellationToken)
    {
        content.TryRemove(packageVersionId, out _);
        return Task.CompletedTask;
    }
}