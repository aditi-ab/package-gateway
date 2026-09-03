using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PackageGateway.Application;
using PackageGateway.Domain;

namespace PackageGateway.Infrastructure;

public sealed class AccessTokenService(IGatewayStore store, IOptions<GatewayInfrastructureOptions> options)
    : IAccessTokenService
{
    private readonly byte[] pepper = ValidatePepper(options.Value.TokenPepper);

    public async Task<CreatedAccessToken> CreateAsync(string name, string owner, IReadOnlyCollection<string> scopes,
        DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        if (scopes.Count == 0 || scopes.Any(x => !IsValidScope(x)))
            throw new ArgumentException("At least one valid repository scope is required.", nameof(scopes));
        if (expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Token expiration must be in the future.", nameof(expiresAt));
        foreach (var scope in scopes.Where(x => x != "repository:read"))
        {
            var repositoryId = Guid.Parse(scope.Split(':')[1]);
            _ = await store.FindRepositoryAsync(repositoryId, cancellationToken) ??
                throw new ArgumentException($"Repository {repositoryId} does not exist.", nameof(scopes));
        }

        var tokenId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var secretPart = Base64Url(RandomNumberGenerator.GetBytes(32));
        var secret = $"pgw_{tokenId}_{secretPart}";
        var token = AccessToken.Create(name, tokenId, VerifyHash(secret), owner, scopes, expiresAt);
        await store.AddAccessTokenAsync(token, cancellationToken);
        await store.AddAuditAsync(
            AuditEvent.Create(owner, "AccessTokenCreated", nameof(AccessToken), token.Id.ToString(),
                $"Access token {name} created."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return new CreatedAccessToken(ToDto(token), secret);
    }

    public async Task<bool> ValidateAsync(string token, Guid repositoryId, CancellationToken cancellationToken)
    {
        if (!TryGetTokenId(token, out var tokenId)) return false;
        var stored = await store.FindAccessTokenByTokenIdAsync(tokenId, cancellationToken);
        if (stored is null || !stored.IsActive(DateTimeOffset.UtcNow) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(stored.Verifier),
                Convert.FromHexString(VerifyHash(token)))) return false;
        var scopes = stored.GetScopes();
        if (!scopes.Contains("repository:read") && !scopes.Contains($"repository:{repositoryId}:read")) return false;
        if (stored.MarkUsed(DateTimeOffset.UtcNow)) await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RevokeAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var token = await store.FindAccessTokenAsync(id, cancellationToken) ??
                    throw new KeyNotFoundException("Access token not found.");
        token.Revoke();
        await store.AddAuditAsync(
            AuditEvent.Create(actor, "AccessTokenRevoked", nameof(AccessToken), id.ToString(),
                $"Access token {token.Name} revoked."), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
    }

    public static AccessTokenDto ToDto(AccessToken x)
    {
        return new AccessTokenDto(x.Id, x.Name, x.TokenId, x.Owner, x.GetScopes(), x.CreatedAt, x.ExpiresAt,
            x.LastUsedAt, x.Enabled);
    }

    private string VerifyHash(string token)
    {
        using var hmac = new HMACSHA256(pepper);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token)));
    }

    private static bool TryGetTokenId(string token, out string tokenId)
    {
        tokenId = string.Empty;
        const int prefixLength = 4;
        const int identifierLength = 16;
        if (!token.StartsWith("pgw_", StringComparison.Ordinal) ||
            token.Length <= prefixLength + identifierLength + 1 ||
            token[prefixLength + identifierLength] != '_') return false;
        var identifier = token.AsSpan(prefixLength, identifierLength);
        if (identifier.IndexOfAnyExcept("0123456789abcdef") >= 0) return false;
        tokenId = identifier.ToString();
        return true;
    }

    private static bool IsValidScope(string scope)
    {
        if (scope == "repository:read") return true;
        var parts = scope.Split(':');
        return parts.Length == 3 && parts[0] == "repository" && Guid.TryParse(parts[1], out _) && parts[2] == "read";
    }

    private static byte[] ValidatePepper(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 32)
            throw new InvalidOperationException("Gateway:TokenPepper must contain at least 32 characters.");
        return Encoding.UTF8.GetBytes(value);
    }

    private static string Base64Url(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}