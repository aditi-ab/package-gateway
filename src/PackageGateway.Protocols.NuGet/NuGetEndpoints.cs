using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using PackageGateway.Application;
using PackageGateway.Domain;

namespace PackageGateway.Protocols.NuGet;

public static class NuGetEndpoints
{
    public static IServiceCollection AddNuGetProtocol(this IServiceCollection services)
    {
        services.AddMemoryCache(options => options.SizeLimit = 10_000);
        services.AddHttpClient<NuGetUpstreamService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                { AutomaticDecompression = DecompressionMethods.All }).AddStandardResilienceHandler();
        services.AddScoped<IUpstreamClient>(sp => sp.GetRequiredService<NuGetUpstreamService>());
        return services;
    }

    public static IEndpointRouteBuilder MapNuGetProtocol(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/nuget/{repository}/v3");
        group.RequireRateLimiting("package");
        group.MapMethods("/index.json", ["GET", "HEAD"], ServiceIndexAsync);
        group.MapMethods("/flatcontainer/{id}/index.json", ["GET", "HEAD"], VersionsAsync);
        group.MapMethods("/flatcontainer/{id}/{version}/{file}", ["GET", "HEAD"], PackageAsync);
        group.MapMethods("/registration/{**path}", ["GET", "HEAD"], RegistrationAsync);
        group.MapMethods("/search", ["GET", "HEAD"], SearchAsync);
        return endpoints;
    }

    private static async Task<IResult> ServiceIndexAsync(string repository, HttpContext context, IGatewayStore store,
        IAccessTokenService tokens, CancellationToken ct)
    {
        var repo = await AuthorizeAsync(repository, context, store, tokens, ct);
        if (repo is null) return Unauthorized(context);
        var root = Absolute(context, $"/nuget/{repo.Slug}/v3");
        return Results.Json(new
        {
            version = "3.0.0", resources = new object[]
            {
                new Dictionary<string, object>
                    { ["@id"] = $"{root}/flatcontainer/", ["@type"] = "PackageBaseAddress/3.0.0" },
                new Dictionary<string, object>
                    { ["@id"] = $"{root}/registration/", ["@type"] = "RegistrationsBaseUrl/3.6.0" },
                new Dictionary<string, object> { ["@id"] = $"{root}/search", ["@type"] = "SearchQueryService/3.5.0" },
                new Dictionary<string, object>
                    { ["@id"] = $"{root}/search", ["@type"] = "SearchQueryService/3.0.0-beta" }
            }
        });
    }

    private static async Task<IResult> VersionsAsync(string repository, string id, HttpContext context,
        IGatewayStore store, IAccessTokenService tokens, NuGetUpstreamService upstreams, CancellationToken ct)
    {
        var repo = await AuthorizeAsync(repository, context, store, tokens, ct);
        if (repo is null) return Unauthorized(context);
        var result = await upstreams.GetVersionsAsync(repo.Id, id, ct);
        return result is null ? Results.NotFound() : JsonResult(result, context.Request.Method == "HEAD");
    }

    private static async Task<IResult> PackageAsync(string repository, string id, string version, string file,
        HttpContext context, IGatewayStore store, IAccessTokenService tokens, IUpstreamResolver upstreams,
        IPackageAcquisitionCoordinator coordinator, CancellationToken ct)
    {
        var repo = await AuthorizeAsync(repository, context, store, tokens, ct);
        if (repo is null) return Unauthorized(context);
        if (!NuGetVersion.TryParse(version, out var parsedVersion)) return Results.BadRequest();
        var normalizedVersion = parsedVersion.ToNormalizedString().ToLowerInvariant();
        var normalizedId = PackageIdentity.Normalize(id, PackageType.NuGet);
        var expectedFile = $"{normalizedId}.{normalizedVersion}.nupkg";
        if (!string.Equals(file, expectedFile, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
        var pinned =
            await store.FindPackageVersionAsync(repo.Id, PackageType.NuGet, normalizedId, normalizedVersion, ct);
        if (pinned is not null)
        {
            var stored = pinned.Value.Version;
            var local = await coordinator.GetOrAcquireAsync(
                new ArtifactRequest(repo.Id, repo.Slug, PackageType.NuGet, pinned.Value.Package.Name, stored.Version,
                    stored.UpstreamId, new Uri(stored.ArtifactUrl), stored.PublishedAt, stored.ExpectedSha256,
                    stored.ExpectedIntegrity), ct);
            return DeliveryResult(local, context, file, context.Request.Method == "HEAD");
        }

        var source = await upstreams.ResolveExactAsync(repo.Id, PackageType.NuGet, id, normalizedVersion, ct);
        if (source is null) return Results.NotFound();
        var delivery = await coordinator.GetOrAcquireAsync(
            new ArtifactRequest(repo.Id, repo.Slug, PackageType.NuGet, id, normalizedVersion, source.UpstreamId,
                source.ArtifactUri, source.PublishedAt, source.ExpectedSha256, source.ExpectedIntegrity), ct);
        return DeliveryResult(delivery, context, file, context.Request.Method == "HEAD");
    }

    private static async Task<IResult> RegistrationAsync(string repository, string? path, HttpContext context,
        IGatewayStore store, IAccessTokenService tokens, NuGetUpstreamService upstreams, CancellationToken ct)
    {
        var repo = await AuthorizeAsync(repository, context, store, tokens, ct);
        if (repo is null) return Unauthorized(context);
        if (string.IsNullOrWhiteSpace(path)) return Results.NotFound();
        var result = await upstreams.GetRegistrationAsync(repo, path, Absolute(context, $"/nuget/{repo.Slug}/v3"), ct);
        return result is null ? Results.NotFound() : JsonResult(result, context.Request.Method == "HEAD");
    }

    private static async Task<IResult> SearchAsync(string repository, HttpContext context, IGatewayStore store,
        IAccessTokenService tokens, NuGetUpstreamService upstreams, CancellationToken ct)
    {
        var repo = await AuthorizeAsync(repository, context, store, tokens, ct);
        if (repo is null) return Unauthorized(context);
        var result = await upstreams.SearchAsync(repo, context.Request.QueryString.Value,
            Absolute(context, $"/nuget/{repo.Slug}/v3"), ct);
        return result is null
            ? Results.StatusCode(StatusCodes.Status502BadGateway)
            : JsonResult(result, context.Request.Method == "HEAD");
    }

    private static IResult DeliveryResult(ArtifactDelivery delivery, HttpContext context, string downloadName,
        bool head)
    {
        if (delivery.Status == ArtifactDeliveryStatus.Pending)
        {
            context.Response.Headers.RetryAfter = "15";
            return Results.Problem(delivery.Message, statusCode: 503);
        }

        if (delivery.Status == ArtifactDeliveryStatus.Denied) return Results.Problem(delivery.Message, statusCode: 403);
        if (delivery.Status == ArtifactDeliveryStatus.NotFound) return Results.NotFound();
        if (delivery.Status != ArtifactDeliveryStatus.Approved || delivery.Content is null)
            return Results.Problem(delivery.Message, statusCode: 502);
        context.Response.Headers.ETag = $"\"{delivery.Sha256}\"";
        context.Response.ContentLength = delivery.Length;
        context.Response.ContentType = delivery.ContentType;
        if (head)
        {
            delivery.Content.Dispose();
            return Results.Empty;
        }

        return Results.Stream(delivery.Content, delivery.ContentType, downloadName, enableRangeProcessing: true);
    }

    private static IResult JsonResult(JsonNode node, bool head)
    {
        return head ? Results.Empty : Results.Text(node.ToJsonString(), "application/json", Encoding.UTF8);
    }

    private static async Task<Repository?> AuthorizeAsync(string slug, HttpContext context, IGatewayStore store,
        IAccessTokenService tokens, CancellationToken ct)
    {
        var repository = await store.FindRepositoryBySlugAsync(slug, PackageType.NuGet, ct);
        if (repository is null) return null;
        var token = ExtractToken(context.Request.Headers.Authorization);
        return token is not null && await tokens.ValidateAsync(token, repository.Id, ct) ? repository : null;
    }

    private static string? ExtractToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return value[7..].Trim();
        if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value[6..].Trim()));
            var separator = decoded.IndexOf(':');
            return separator >= 0 ? decoded[(separator + 1)..] : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IResult Unauthorized(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Package Gateway\"";
        return Results.Unauthorized();
    }

    private static string Absolute(HttpContext context, string path)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{path}";
    }
}

public sealed class NuGetUpstreamService(HttpClient httpClient, IGatewayStore store, IMemoryCache cache)
    : IUpstreamClient
{
    public PackageType PackageType => PackageType.NuGet;

    public async Task<ResolvedArtifact?> ResolveExactAsync(Upstream upstream, string packageName, string version,
        CancellationToken ct)
    {
        var resources = await GetResourcesAsync(upstream, ct);
        if (resources?.PackageBase is null) return null;
        var normalizedId = packageName.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var versions = await GetJsonAsync(new Uri(resources.PackageBase, $"{normalizedId}/index.json"), ct);
        if (versions?["versions"] is not JsonArray array || !array.Any(x =>
                string.Equals(x?.GetValue<string>(), normalizedVersion, StringComparison.OrdinalIgnoreCase)))
            return null;
        var published = await FindPublishedAtAsync(resources, normalizedId, normalizedVersion, ct);
        return new ResolvedArtifact(upstream.Id,
            new Uri(resources.PackageBase,
                $"{normalizedId}/{normalizedVersion}/{normalizedId}.{normalizedVersion}.nupkg"), published);
    }

    public async Task<IReadOnlyList<UpstreamPackageDto>> SearchAsync(Upstream upstream, string query, int take,
        CancellationToken ct)
    {
        var resources = await GetResourcesAsync(upstream, ct);
        if (resources?.Search is null) return [];
        var uri = new UriBuilder(resources.Search)
        {
            Query = $"q={Uri.EscapeDataString(query)}&take={Math.Clamp(take, 1, 50)}&prerelease=true&semVerLevel=2.0.0"
        }.Uri;
        var node = await GetJsonAsync(uri, ct);
        if (node?["data"] is not JsonArray data) return [];
        return data.OfType<JsonObject>().Select(item =>
        {
            var name = item["id"]?.GetValue<string>();
            var version = item["version"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)
                ? null
                : new UpstreamPackageDto(upstream.Id, upstream.Name, PackageType.NuGet, name, version,
                    item["description"]?.GetValue<string>(), null);
        }).Where(x => x is not null).Cast<UpstreamPackageDto>().Take(take).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetVersionsAsync(Upstream upstream, string packageName,
        CancellationToken ct)
    {
        var resources = await GetResourcesAsync(upstream, ct);
        if (resources?.PackageBase is null) return [];
        var node = await GetJsonAsync(new Uri(resources.PackageBase,
            $"{packageName.ToLowerInvariant()}/index.json"), ct);
        return node?["versions"] is JsonArray versions
            ? versions.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
                .Reverse().ToArray()
            : [];
    }

    public async Task<JsonNode?> GetVersionsAsync(Guid repositoryId, string id, CancellationToken ct)
    {
        foreach (var upstream in (await store.GetUpstreamsAsync(repositoryId, PackageType.NuGet, ct)).Where(x =>
                     x.Enabled))
        {
            var resources = await GetResourcesAsync(upstream, ct);
            if (resources?.PackageBase is null) continue;
            var node = await GetJsonAsync(new Uri(resources.PackageBase, $"{id.ToLowerInvariant()}/index.json"), ct);
            if (node is not null) return node;
        }

        return null;
    }

    public async Task<NuGetArtifactSource?> ResolveArtifactAsync(Guid repositoryId, string id, string version,
        CancellationToken ct)
    {
        foreach (var upstream in (await store.GetUpstreamsAsync(repositoryId, PackageType.NuGet, ct)).Where(x =>
                     x.Enabled))
        {
            var resources = await GetResourcesAsync(upstream, ct);
            if (resources?.PackageBase is null) continue;
            var normalizedId = id.ToLowerInvariant();
            var normalizedVersion = version.ToLowerInvariant();
            var versions = await GetJsonAsync(new Uri(resources.PackageBase, $"{normalizedId}/index.json"), ct);
            if (versions?["versions"] is not JsonArray array || !array.Any(x =>
                    string.Equals(x?.GetValue<string>(), normalizedVersion, StringComparison.OrdinalIgnoreCase)))
                continue;
            var published = await FindPublishedAtAsync(resources, normalizedId, normalizedVersion, ct);
            return new NuGetArtifactSource(upstream,
                new Uri(resources.PackageBase,
                    $"{normalizedId}/{normalizedVersion}/{normalizedId}.{normalizedVersion}.nupkg"), published);
        }

        return null;
    }

    public async Task<JsonNode?> GetRegistrationAsync(Repository repository, string path, string localRoot,
        CancellationToken ct)
    {
        foreach (var upstream in (await store.GetUpstreamsAsync(repository.Id, PackageType.NuGet, ct)).Where(x =>
                     x.Enabled))
        {
            var resources = await GetResourcesAsync(upstream, ct);
            if (resources?.RegistrationBase is null) continue;
            var node = await GetJsonAsync(new Uri(resources.RegistrationBase, path), ct);
            if (node is null) continue;
            Rewrite(node, resources, localRoot);
            return node;
        }

        return null;
    }

    public async Task<JsonNode?> SearchAsync(Repository repository, string? query, string localRoot,
        CancellationToken ct)
    {
        foreach (var upstream in (await store.GetUpstreamsAsync(repository.Id, PackageType.NuGet, ct)).Where(x =>
                     x.Enabled))
        {
            var resources = await GetResourcesAsync(upstream, ct);
            if (resources?.Search is null) continue;
            var builder = new UriBuilder(resources.Search) { Query = query?.TrimStart('?') ?? string.Empty };
            var node = await GetJsonAsync(builder.Uri, ct);
            if (node is null) continue;
            Rewrite(node, resources, localRoot);
            return node;
        }

        return null;
    }

    private async Task<NuGetResources?> GetResourcesAsync(Upstream upstream, CancellationToken ct)
    {
        var index = await GetJsonAsync(new Uri(upstream.Url), ct);
        if (index?["resources"] is not JsonArray resources) return null;
        Uri? package = null, registration = null, search = null;
        foreach (var resource in resources.OfType<JsonObject>())
        {
            var type = resource["@type"]?.GetValue<string>();
            var url = resource["@id"]?.GetValue<string>();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            if (type?.StartsWith("PackageBaseAddress/", StringComparison.Ordinal) == true) package ??= EnsureSlash(uri);
            else if (type?.StartsWith("RegistrationsBaseUrl/", StringComparison.Ordinal) == true)
                registration = PreferRegistration(registration, uri, type);
            else if (type?.StartsWith("SearchQueryService/", StringComparison.Ordinal) == true) search ??= uri;
        }

        return package is null
            ? null
            : new NuGetResources(package, registration is null ? null : EnsureSlash(registration), search);
    }

    private async Task<DateTimeOffset?> FindPublishedAtAsync(NuGetResources resources, string id, string version,
        CancellationToken ct)
    {
        if (resources.RegistrationBase is null) return null;
        var node = await GetJsonAsync(new Uri(resources.RegistrationBase, $"{id}/index.json"), ct);
        return FindPublished(node, version);
    }

    private static DateTimeOffset? FindPublished(JsonNode? node, string version)
    {
        if (node is JsonObject obj)
        {
            if (string.Equals(obj["version"]?.GetValue<string>(), version, StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.TryParse(obj["published"]?.GetValue<string>(), out var published)) return published;
            foreach (var child in obj)
            {
                var result = FindPublished(child.Value, version);
                if (result is not null) return result;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                var result = FindPublished(child, version);
                if (result is not null) return result;
            }
        }

        return null;
    }

    private async Task<JsonNode?> GetJsonAsync(Uri uri, CancellationToken ct)
    {
        var key = $"nuget:{uri.AbsoluteUri}";
        cache.TryGetValue<MetadataCacheEntry>(key, out var cached);
        if (cached is not null && cached.FreshUntil > DateTimeOffset.UtcNow)
        {
            GatewayDiagnostics.MetadataCacheOutcomes.Add(1, new KeyValuePair<string, object?>("protocol", "nuget"),
                new KeyValuePair<string, object?>("outcome", "hit"));
            return JsonNode.Parse(cached.Json);
        }

        GatewayDiagnostics.MetadataCacheOutcomes.Add(1, new KeyValuePair<string, object?>("protocol", "nuget"),
            new KeyValuePair<string, object?>("outcome", cached is null ? "miss" : "stale"));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (cached?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (cached?.LastModified is { } modified) request.Headers.IfModifiedSince = modified;
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            GatewayDiagnostics.MetadataCacheOutcomes.Add(1, new KeyValuePair<string, object?>("protocol", "nuget"),
                new KeyValuePair<string, object?>("outcome", "revalidated"));
            StoreCache(key, cached with { FreshUntil = DateTimeOffset.UtcNow.AddMinutes(5) });
            return JsonNode.Parse(cached.Json);
        }

        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(20L * 1024 * 1024, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var entry = new MetadataCacheEntry(json, response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified, DateTimeOffset.UtcNow.AddMinutes(5));
        StoreCache(key, entry);
        return JsonNode.Parse(json);
    }

    private void StoreCache(string key, MetadataCacheEntry entry)
    {
        cache.Set(key, entry,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30), Size = 1 });
    }

    private static void Rewrite(JsonNode node, NuGetResources resources, string localRoot)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    if (resources.PackageBase is not null && text.StartsWith(resources.PackageBase.AbsoluteUri,
                            StringComparison.OrdinalIgnoreCase))
                        obj[property.Key] =
                            $"{localRoot}/flatcontainer/{text[resources.PackageBase.AbsoluteUri.Length..]}";
                    else if (resources.RegistrationBase is not null &&
                             text.StartsWith(resources.RegistrationBase.AbsoluteUri,
                                 StringComparison.OrdinalIgnoreCase))
                        obj[property.Key] =
                            $"{localRoot}/registration/{text[resources.RegistrationBase.AbsoluteUri.Length..]}";
                }
                else if (property.Value is not null)
                {
                    Rewrite(property.Value, resources, localRoot);
                }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.Where(x => x is not null)) Rewrite(item!, resources, localRoot);
        }
    }

    private static Uri EnsureSlash(Uri uri)
    {
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    private static Uri PreferRegistration(Uri? current, Uri candidate, string type)
    {
        return current is null || type.Contains("3.6.0", StringComparison.Ordinal) ? candidate : current;
    }

    private sealed record NuGetResources(Uri PackageBase, Uri? RegistrationBase, Uri? Search);

    private sealed record MetadataCacheEntry(
        string Json,
        string? ETag,
        DateTimeOffset? LastModified,
        DateTimeOffset FreshUntil);
}

public sealed record NuGetArtifactSource(Upstream Upstream, Uri ArtifactUri, DateTimeOffset? PublishedAt);