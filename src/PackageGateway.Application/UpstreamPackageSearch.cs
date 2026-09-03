using PackageGateway.Domain;

namespace PackageGateway.Application;

public sealed class UpstreamPackageSearch(IGatewayStore store, IEnumerable<IUpstreamClient> clients)
    : IUpstreamPackageSearch
{
    public async Task<IReadOnlyList<UpstreamPackageDto>> SearchAsync(Guid repositoryId, PackageType packageType,
        string query, int take, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            throw new ArgumentException("Enter at least two characters to search upstream packages.", nameof(query));
        var repository = await store.FindRepositoryAsync(repositoryId, cancellationToken) ??
                         throw new KeyNotFoundException("Repository not found.");
        if (!repository.Enabled) throw new InvalidOperationException("Repository is disabled.");
        var client = clients.SingleOrDefault(x => x.PackageType == packageType) ??
                     throw new InvalidOperationException($"No upstream client is registered for {packageType}.");
        var limit = Math.Clamp(take, 1, 50);
        var results = new List<UpstreamPackageDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var upstream in (await store.GetUpstreamsAsync(repositoryId, packageType, cancellationToken))
                 .Where(x => x.Enabled))
        foreach (var package in await client.SearchAsync(upstream, query.Trim(), limit, cancellationToken))
        {
            var key = $"{package.PackageType}:{PackageIdentity.Normalize(package.Name, package.PackageType)}";
            if (!seen.Add(key)) continue;
            results.Add(package);
            if (results.Count == limit) return results;
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetVersionsAsync(Guid repositoryId, Guid upstreamId,
        PackageType packageType, string packageName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            throw new ArgumentException("Package name is required.", nameof(packageName));
        var repository = await store.FindRepositoryAsync(repositoryId, cancellationToken) ??
                         throw new KeyNotFoundException("Repository not found.");
        if (!repository.Enabled) throw new InvalidOperationException("Repository is disabled.");
        var upstream = await store.FindUpstreamAsync(upstreamId, cancellationToken) ??
                       throw new KeyNotFoundException("Upstream not found.");
        if (upstream.RepositoryId != repositoryId || upstream.PackageType != packageType || !upstream.Enabled)
            throw new InvalidOperationException("The upstream is not enabled for this repository and format.");
        var client = clients.SingleOrDefault(x => x.PackageType == packageType) ??
                     throw new InvalidOperationException($"No upstream client is registered for {packageType}.");
        return await client.GetVersionsAsync(upstream, packageName.Trim(), cancellationToken);
    }
}