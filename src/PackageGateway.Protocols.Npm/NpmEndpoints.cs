using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Application;
using PackageGateway.Domain;

namespace PackageGateway.Protocols.Npm;

public static class NpmEndpoints
{
    public static IServiceCollection AddNpmProtocol(this IServiceCollection services)
    {
        services.AddMemoryCache(options => options.SizeLimit = 10_000);
        services.AddHttpClient<NpmUpstreamService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                { AutomaticDecompression = DecompressionMethods.All }).AddStandardResilienceHandler();
        services.AddScoped<IUpstreamClient>(sp => sp.GetRequiredService<NpmUpstreamService>());
        return services;
    }

    public static IEndpointRouteBuilder MapNpmProtocol(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/npm/{repository}/{**path}", ["GET", "HEAD"], HandleAsync).RequireRateLimiting("package");
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(string repository, string? path, HttpContext context,
        IGatewayStore store, IAccessTokenService tokens, NpmUpstreamService upstreams, IUpstreamResolver resolver,
        IPackageAcquisitionCoordinator coordinator, CancellationToken ct)
    {
        var repo = await store.FindRepositoryBySlugAsync(repository, PackageType.Npm, ct);
        if (repo is null) return Results.NotFound();
        var token = ExtractToken(context.Request.Headers.Authorization);
        if (token is null || !await tokens.ValidateAsync(token, repo.Id, ct))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer realm=\"Package Gateway\"";
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(path))
            return Results.Json(new { name = "Package Gateway", repository = repo.Slug });
        var decoded = Uri.UnescapeDataString(path).Trim('/');
        var marker = decoded.IndexOf("/-/", StringComparison.Ordinal);
        if (marker < 0)
        {
            var metadata = await upstreams.GetMetadataAsync(repo, decoded, Absolute(context, $"/npm/{repo.Slug}"), ct);
            return metadata is null ? Results.NotFound() : Json(metadata, context.Request.Method == "HEAD");
        }

        var packageName = decoded[..marker];
        var artifactPath = decoded[(marker + 3)..].Split('/', 2);
        if (artifactPath.Length != 2) return Results.NotFound();
        var requestedVersion = artifactPath[0];
        var file = artifactPath[1];
        var normalizedName = PackageIdentity.Normalize(packageName, PackageType.Npm);
        var pinned =
            await store.FindPackageVersionAsync(repo.Id, PackageType.Npm, normalizedName, requestedVersion, ct);
        if (pinned is not null)
        {
            var stored = pinned.Value.Version;
            var expectedFile = Path.GetFileName(new Uri(stored.ArtifactUrl).AbsolutePath);
            if (!string.Equals(expectedFile, file, StringComparison.Ordinal)) return Results.NotFound();
            var local = await coordinator.GetOrAcquireAsync(
                new ArtifactRequest(repo.Id, repo.Slug, PackageType.Npm, pinned.Value.Package.Name, stored.Version,
                    stored.UpstreamId, new Uri(stored.ArtifactUrl), stored.PublishedAt, stored.ExpectedSha256,
                    stored.ExpectedIntegrity), ct);
            return Delivery(local, context, file, context.Request.Method == "HEAD");
        }

        var source = await resolver.ResolveExactAsync(repo.Id, PackageType.Npm, packageName, requestedVersion, ct);
        if (source is null || !string.Equals(Path.GetFileName(source.ArtifactUri.AbsolutePath), file,
                StringComparison.Ordinal)) return Results.NotFound();
        var delivery = await coordinator.GetOrAcquireAsync(
            new ArtifactRequest(repo.Id, repo.Slug, PackageType.Npm, packageName, requestedVersion, source.UpstreamId,
                source.ArtifactUri, source.PublishedAt, source.ExpectedSha256, source.ExpectedIntegrity), ct);
        return Delivery(delivery, context, file, context.Request.Method == "HEAD");
    }

    private static IResult Delivery(ArtifactDelivery delivery, HttpContext context, string file, bool head)
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

        return Results.Stream(delivery.Content, delivery.ContentType, file, enableRangeProcessing: true);
    }

    private static IResult Json(JsonNode node, bool head)
    {
        return head ? Results.Empty : Results.Text(node.ToJsonString(), "application/json", Encoding.UTF8);
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

    private static string Absolute(HttpContext context, string path)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{path}";
    }
}

public sealed class NpmUpstreamService(HttpClient httpClient, IGatewayStore store, IMemoryCache cache) : IUpstreamClient
{
    public PackageType PackageType => PackageType.Npm;

    public async Task<ResolvedArtifact?> ResolveExactAsync(Upstream upstream, string packageName, string version,
        CancellationToken ct)
    {
        var metadata = await GetMetadataFromAsync(upstream, packageName, ct);
        if (metadata is not JsonObject root || root["versions"] is not JsonObject versions ||
            versions[version] is not JsonObject item || item["dist"] is not JsonObject dist) return null;
        var tarball = dist["tarball"]?.GetValue<string>();
        if (!Uri.TryCreate(tarball, UriKind.Absolute, out var uri)) return null;
        var published = root["time"]?[version]?.GetValue<DateTimeOffset?>();
        var integrity = dist["integrity"]?.GetValue<string>();
        return new ResolvedArtifact(upstream.Id, uri, published, ExpectedIntegrity: integrity);
    }

    public async Task<IReadOnlyList<UpstreamPackageDto>> SearchAsync(Upstream upstream, string query, int take,
        CancellationToken ct)
    {
        var baseUri = upstream.Url.EndsWith('/') ? new Uri(upstream.Url) : new Uri(upstream.Url + "/");
        var uri = new Uri(baseUri,
            $"-/v1/search?text={Uri.EscapeDataString(query)}&size={Math.Clamp(take, 1, 50)}");
        var node = await GetJsonAsync(uri, $"npm-search:{upstream.Id:N}:{query}:{take}", ct);
        if (node?["objects"] is not JsonArray objects) return [];
        return objects.OfType<JsonObject>().Select(item => item["package"] as JsonObject).Where(x => x is not null)
            .Select(package =>
            {
                var name = package!["name"]?.GetValue<string>();
                var version = package["version"]?.GetValue<string>();
                DateTimeOffset? published = null;
                if (DateTimeOffset.TryParse(package["date"]?.GetValue<string>(), out var parsed)) published = parsed;
                return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)
                    ? null
                    : new UpstreamPackageDto(upstream.Id, upstream.Name, PackageType.Npm, name, version,
                        package["description"]?.GetValue<string>(), published);
            }).Where(x => x is not null).Cast<UpstreamPackageDto>().Take(take).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetVersionsAsync(Upstream upstream, string packageName,
        CancellationToken ct)
    {
        var metadata = await GetMetadataFromAsync(upstream, packageName, ct);
        return metadata?["versions"] is JsonObject versions
            ? versions.Select(x => x.Key).Reverse().ToArray()
            : [];
    }

    public async Task<JsonNode?> GetMetadataAsync(Repository repository, string packageName, string localRoot,
        CancellationToken ct)
    {
        foreach (var upstream in (await store.GetUpstreamsAsync(repository.Id, PackageType.Npm, ct)).Where(x =>
                     x.Enabled))
        {
            var metadata = await GetMetadataFromAsync(upstream, packageName, ct);
            if (metadata is null) continue;
            NpmUrlRewriter.RewriteTarballs(metadata, packageName, localRoot);
            return metadata;
        }

        return null;
    }

    public async Task<NpmArtifactSource?> ResolveTarballAsync(Guid repositoryId, string packageName,
        string requestedVersion, string requestedFile, CancellationToken ct)
    {
        foreach (var upstream in
                 (await store.GetUpstreamsAsync(repositoryId, PackageType.Npm, ct)).Where(x => x.Enabled))
        {
            var metadata = await GetMetadataFromAsync(upstream, packageName, ct);
            if (metadata is not JsonObject root || root["versions"] is not JsonObject versions) continue;
            if (versions[requestedVersion] is not JsonObject version ||
                version["dist"] is not JsonObject dist) continue;
            var tarball = dist["tarball"]?.GetValue<string>();
            if (!Uri.TryCreate(tarball, UriKind.Absolute, out var uri) ||
                !string.Equals(Path.GetFileName(uri.AbsolutePath), requestedFile, StringComparison.Ordinal)) continue;
            var published = root["time"]?[requestedVersion]?.GetValue<DateTimeOffset?>();
            var integrity = dist["integrity"]?.GetValue<string>();
            return new NpmArtifactSource(upstream, requestedVersion, uri, published, integrity);
        }

        return null;
    }

    private async Task<JsonNode?> GetMetadataFromAsync(Upstream upstream, string packageName, CancellationToken ct)
    {
        var baseUri = upstream.Url.EndsWith('/') ? new Uri(upstream.Url) : new Uri(upstream.Url + "/");
        var uri = new Uri(baseUri, Uri.EscapeDataString(packageName));
        var key = $"npm:{upstream.Id:N}:{packageName}";
        return await GetJsonAsync(uri, key, ct);
    }

    private async Task<JsonNode?> GetJsonAsync(Uri uri, string key, CancellationToken ct)
    {
        cache.TryGetValue<MetadataCacheEntry>(key, out var cached);
        if (cached is not null && cached.FreshUntil > DateTimeOffset.UtcNow)
        {
            GatewayDiagnostics.MetadataCacheOutcomes.Add(1, new KeyValuePair<string, object?>("protocol", "npm"),
                new KeyValuePair<string, object?>("outcome", "hit"));
            return JsonNode.Parse(cached.Json);
        }

        GatewayDiagnostics.MetadataCacheOutcomes.Add(1, new KeyValuePair<string, object?>("protocol", "npm"),
            new KeyValuePair<string, object?>("outcome", cached is null ? "miss" : "stale"));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (cached?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (cached?.LastModified is { } modified) request.Headers.IfModifiedSince = modified;
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            GatewayDiagnostics.MetadataCacheOutcomes.Add(1, new KeyValuePair<string, object?>("protocol", "npm"),
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

    private sealed record MetadataCacheEntry(
        string Json,
        string? ETag,
        DateTimeOffset? LastModified,
        DateTimeOffset FreshUntil);
}

public static class NpmUrlRewriter
{
    public static void RewriteTarballs(JsonNode node, string packageName, string localRoot)
    {
        if (node is not JsonObject root || root["versions"] is not JsonObject versions) return;
        foreach (var versionProperty in versions)
        {
            if (versionProperty.Value is not JsonObject version || version["dist"] is not JsonObject dist ||
                dist["tarball"] is not JsonValue value || !value.TryGetValue<string>(out var url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            dist["tarball"] =
                $"{localRoot}/{Uri.EscapeDataString(packageName)}/-/{Uri.EscapeDataString(versionProperty.Key)}/{Path.GetFileName(uri.AbsolutePath)}";
        }
    }
}

public sealed record NpmArtifactSource(
    Upstream Upstream,
    string Version,
    Uri ArtifactUri,
    DateTimeOffset? PublishedAt,
    string? Integrity);