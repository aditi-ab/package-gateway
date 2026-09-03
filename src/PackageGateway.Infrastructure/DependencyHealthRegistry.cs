using System.Collections.Concurrent;

namespace PackageGateway.Infrastructure;

public sealed record DependencyStatus(
    string Name,
    bool Healthy,
    bool UsingCachedData,
    DateTimeOffset CheckedAt,
    string? Detail);

public sealed class DependencyHealthRegistry
{
    private readonly ConcurrentDictionary<string, DependencyStatus> values = new(StringComparer.Ordinal);

    public void Report(string name, bool healthy, bool usingCachedData, string? detail = null)
    {
        values[name] = new DependencyStatus(name, healthy, usingCachedData, DateTimeOffset.UtcNow, detail);
    }

    public IReadOnlyList<DependencyStatus> GetAll()
    {
        return values.Values.OrderBy(x => x.Name).ToArray();
    }
}