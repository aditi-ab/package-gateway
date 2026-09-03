using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aditify.Identity;
using Microsoft.EntityFrameworkCore;
using PackageGateway.Domain;
using PackageGateway.Storage;

namespace PackageGateway.Api;

public sealed class GatewayAdminIdentityStore(GatewayDbContext db) : IAdminIdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public async Task<IReadOnlyList<AdminIdentityUser>> ListUsersAsync(CancellationToken ct) => (await db.LocalAdministrators.AsNoTracking().OrderBy(x => x.NormalizedUsername).ToArrayAsync(ct)).Select(x => Map(x)!).ToArray();
    public async Task<AdminIdentityUser?> FindUserAsync(Guid id, CancellationToken ct) => Map(await db.LocalAdministrators.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct));
    public async Task<AdminIdentityUser?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken ct) => Map(await db.LocalAdministrators.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUsername == normalizedUsername, ct));
    public async Task SaveUserAsync(AdminIdentityUser user, CancellationToken ct)
    {
        var entity = await db.LocalAdministrators.SingleOrDefaultAsync(x => x.Id == user.Id, ct);
        if (entity is null) { entity = LocalAdministrator.Create(user.Username, user.NormalizedUsername, user.PasswordHash); db.LocalAdministrators.Add(entity); }
        var roles = user.RoleGrants.Select(x => x.Role).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        entity.ApplyIdentityState(user.Id, user.DisplayName, user.PasswordHash, roles, user.Enabled, user.MustChangePassword,
            user.SecurityStamp, Guid.Parse(user.Version), user.LastLoginAt, JsonSerializer.Serialize(user.ExternalIdentities, JsonOptions), JsonSerializer.Serialize(user.RoleGrants, JsonOptions));
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteUserAsync(Guid id, CancellationToken ct) { var x = await db.LocalAdministrators.SingleOrDefaultAsync(v => v.Id == id, ct); if (x is null) return; db.LocalAdministrators.Remove(x); await db.SaveChangesAsync(ct); }
    public async Task<IReadOnlyList<AdminIdentityProvider>> ListProvidersAsync(CancellationToken ct) => (await db.AdminIdentityProviders.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Json).ToArrayAsync(ct)).Select(Provider).ToArray();
    public async Task<AdminIdentityProvider?> FindProviderAsync(string id, CancellationToken ct) { var normalized = id.Trim().ToLowerInvariant(); var json = await db.AdminIdentityProviders.AsNoTracking().Where(x => x.Id == normalized).Select(x => x.Json).SingleOrDefaultAsync(ct); return json is null ? null : Provider(json); }
    public async Task SaveProviderAsync(AdminIdentityProvider provider, CancellationToken ct) { var id = provider.Id.Trim().ToLowerInvariant(); var json = JsonSerializer.Serialize(provider, JsonOptions); var x = await db.AdminIdentityProviders.SingleOrDefaultAsync(v => v.Id == id, ct); if (x is null) db.AdminIdentityProviders.Add(AdminIdentityProviderDocument.Create(id, json)); else x.Update(json); await db.SaveChangesAsync(ct); }
    public async Task DeleteProviderAsync(string id, CancellationToken ct) { var normalized = id.Trim().ToLowerInvariant(); var x = await db.AdminIdentityProviders.SingleOrDefaultAsync(v => v.Id == normalized, ct); if (x is null) return; db.AdminIdentityProviders.Remove(x); await db.SaveChangesAsync(ct); }
    private static AdminIdentityUser? Map(LocalAdministrator? x) { if (x is null) return null; var grants = Deserialize<AdminRoleGrant>(x.RoleGrantsJson); if (grants.Count == 0) grants = LocalAuthenticationService.UserRoles(x).Select(role => new AdminRoleGrant(role, "local")).ToList(); return new AdminIdentityUser { Id = x.Id, Username = x.Username, NormalizedUsername = x.NormalizedUsername, DisplayName = x.DisplayName, PasswordHash = x.PasswordHash, SecurityStamp = x.SecurityStamp, Enabled = x.Enabled, MustChangePassword = x.MustChangePassword, LastLoginAt = x.LastLoginAt, Version = x.ConcurrencyToken.ToString("D"), ExternalIdentities = Deserialize<AdminExternalIdentity>(x.ExternalIdentitiesJson), RoleGrants = grants }; }
    private static List<T> Deserialize<T>(string json) => string.IsNullOrWhiteSpace(json)
        ? []
        : JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
    private static AdminIdentityProvider Provider(string json) => JsonSerializer.Deserialize<AdminIdentityProvider>(json, JsonOptions)!;
}
public sealed class GatewayRoleCatalog : IProductRoleCatalog { public IReadOnlyList<string> Roles => LocalAuthenticationService.Roles; }
public sealed class GatewayIdentityAuditSink(GatewayDbContext db) : IAdminIdentityAuditSink
{
    public async Task WriteAsync(string action, string target, string outcome, ClaimsPrincipal? actor, CancellationToken ct) { db.AuditEvents.Add(AuditEvent.Create(actor?.Identity?.Name ?? "system", action, "Identity", target, outcome)); await db.SaveChangesAsync(ct); }
}
