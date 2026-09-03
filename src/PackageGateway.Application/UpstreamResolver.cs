using PackageGateway.Domain;

namespace PackageGateway.Application;

public sealed class UpstreamResolver(IGatewayStore store, IEnumerable<IUpstreamClient> clients) : IUpstreamResolver
{
    public async Task<ResolvedArtifact?> ResolveExactAsync(Guid repositoryId, PackageType packageType,
        string packageName, string version, CancellationToken cancellationToken)
    {
        var client = clients.SingleOrDefault(x => x.PackageType == packageType)
                     ?? throw new InvalidOperationException($"No upstream client is registered for {packageType}.");
        foreach (var upstream in
                 (await store.GetUpstreamsAsync(repositoryId, packageType, cancellationToken)).Where(x => x.Enabled))
        {
            var artifact = await client.ResolveExactAsync(upstream, packageName, version, cancellationToken);
            if (artifact is not null) return artifact;
        }

        return null;
    }
}