using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PackageGateway.Domain;
using PackageGateway.Storage;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;

namespace PackageGateway.Api;

public sealed partial class LocalAuthenticationService(GatewayDbContext db, IPasswordHasher<string> passwordHasher)
{
    public const string MustChangePasswordClaim = "packagegateway.must-change-password";

    public static readonly string[] Roles =
    [
        AuthorizationPolicies.ReaderRole, AuthorizationPolicies.SecurityReviewerRole,
        AuthorizationPolicies.RepositoryAdminRole, AuthorizationPolicies.AdministratorRole
    ];

    public async Task<LocalAuthenticationState> GetStateAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        return new LocalAuthenticationState(!await db.LocalAdministrators.AsNoTracking().AnyAsync(cancellationToken),
            principal.Identity?.IsAuthenticated == true, principal.Identity?.Name);
    }

    public async Task<LocalAdministrator> BootstrapAsync(string username, string password,
        CancellationToken cancellationToken)
    {
        username = ValidateUsername(username);
        ValidatePassword(username, password);
        if (await db.LocalAdministrators.AnyAsync(cancellationToken)) throw new LocalBootstrapUnavailableException();

        var normalized = username.ToUpperInvariant();
        var administrator = LocalAdministrator.Create(username, normalized,
            passwordHasher.HashPassword(normalized, password));
        db.LocalAdministrators.Add(administrator);
        db.AuditEvents.Add(AuditEvent.Create(username, "LocalAdministratorBootstrapped", nameof(LocalAdministrator),
            administrator.Id.ToString(), "The local administrator account was created."));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await db.LocalAdministrators.AsNoTracking().AnyAsync(cancellationToken))
                throw new LocalBootstrapUnavailableException();
            throw;
        }

        return administrator;
    }

    public async Task<LocalAdministrator?> ValidateCredentialsAsync(string username, string password,
        CancellationToken cancellationToken)
    {
        var normalized = (username ?? string.Empty).Trim().ToUpperInvariant();
        var administrator = await db.LocalAdministrators.SingleOrDefaultAsync(
            x => x.NormalizedUsername == normalized, cancellationToken);
        if (administrator is null || !administrator.Enabled) return null;
        var result =
            passwordHasher.VerifyHashedPassword(normalized, administrator.PasswordHash, password ?? string.Empty);
        if (!string.Equals(normalized, administrator.NormalizedUsername, StringComparison.Ordinal) ||
            result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            administrator.UpdatePasswordHash(passwordHasher.HashPassword(normalized, password!));
            await db.SaveChangesAsync(cancellationToken);
        }

        administrator.RecordLogin();
        await db.SaveChangesAsync(cancellationToken);

        return administrator;
    }

    public static ClaimsPrincipal CreatePrincipal(LocalAdministrator administrator)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, administrator.Id.ToString()), new(ClaimTypes.Name, administrator.Username),
            new("preferred_username", administrator.Username),
            .. administrator.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role =>
                new Claim(ClaimTypes.Role, role)),
            new("packagegateway.security-stamp", administrator.SecurityStamp),
            new(MustChangePasswordClaim, administrator.MustChangePassword ? "true" : "false")
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, LocalAuthenticationDefaults.CookieScheme, ClaimTypes.Name,
            ClaimTypes.Role));
    }

    public Task<List<LocalAdministrator>> ListAsync(CancellationToken ct)
    {
        return db.LocalAdministrators.AsNoTracking().OrderBy(x => x.Username).ToListAsync(ct);
    }

    public async Task<(LocalAdministrator User, string TemporaryPassword)> CreateAsync(string username,
        IReadOnlyList<string> roles, string actor, CancellationToken ct)
    {
        username = ValidateUsername(username);
        ValidateRoles(roles);
        var password = TemporaryPassword();
        var normalized = username.ToUpperInvariant();
        var user = LocalAdministrator.Create(username, normalized, passwordHasher.HashPassword(normalized, password),
            roles.Distinct().Order().ToArray());
        user.SetPassword(user.PasswordHash, true);
        db.LocalAdministrators.Add(user);
        db.AuditEvents.Add(AuditEvent.Create(actor, "LocalUserCreated", nameof(LocalAdministrator), user.Id.ToString(),
            "A local user was created."));
        await db.SaveChangesAsync(ct);
        return (user, password);
    }

    public async Task<LocalAdministrator> UpdateAsync(Guid id, Guid expectedVersion, IReadOnlyList<string> roles,
        bool enabled, string actor, CancellationToken ct)
    {
        ValidateRoles(roles);
        var user = await Required(id, ct);
        if (user.ConcurrencyToken != expectedVersion) throw new DbUpdateConcurrencyException();
        if (user.Enabled && UserRoles(user).Contains(AuthorizationPolicies.AdministratorRole) &&
            (!enabled || !roles.Contains(AuthorizationPolicies.AdministratorRole))) await EnsureAnotherAdmin(id, ct);
        user.SetAccess(roles.Distinct().Order().ToArray(), enabled);
        db.AuditEvents.Add(AuditEvent.Create(actor, "LocalUserUpdated", nameof(LocalAdministrator), user.Id.ToString(),
            "Local user access was updated."));
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<(LocalAdministrator User, string TemporaryPassword)> ResetAsync(Guid id, string actor,
        CancellationToken ct)
    {
        var user = await Required(id, ct);
        var password = TemporaryPassword();
        user.SetPassword(passwordHasher.HashPassword(user.NormalizedUsername, password), true);
        db.AuditEvents.Add(AuditEvent.Create(actor, "LocalUserPasswordReset", nameof(LocalAdministrator),
            user.Id.ToString(), "A local user password was reset."));
        await db.SaveChangesAsync(ct);
        return (user, password);
    }

    public async Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword, string actor,
        CancellationToken ct)
    {
        var user = await Required(id, ct);
        if (passwordHasher.VerifyHashedPassword(user.NormalizedUsername, user.PasswordHash, currentPassword) ==
            PasswordVerificationResult.Failed)
            throw new LocalAuthenticationValidationException("The current password is incorrect.");
        ValidatePassword(user.Username, newPassword);
        user.SetPassword(passwordHasher.HashPassword(user.NormalizedUsername, newPassword), false);
        db.AuditEvents.Add(AuditEvent.Create(actor, "LocalUserPasswordChanged", nameof(LocalAdministrator),
            user.Id.ToString(), "The local user changed their password."));
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken ct)
    {
        var user = await Required(id, ct);
        if (user.Enabled && UserRoles(user).Contains(AuthorizationPolicies.AdministratorRole))
            await EnsureAnotherAdmin(id, ct);
        db.LocalAdministrators.Remove(user);
        db.AuditEvents.Add(AuditEvent.Create(actor, "LocalUserDeleted", nameof(LocalAdministrator), user.Id.ToString(),
            "A local user was deleted."));
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ValidatePrincipalAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var stamp = principal.FindFirstValue("packagegateway.security-stamp");
        return await db.LocalAdministrators.AsNoTracking().AnyAsync(x => x.Id == id && x.Enabled &&
                                                                         x.SecurityStamp == stamp, ct);
    }

    public static string[] UserRoles(LocalAdministrator user)
    {
        return user.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<LocalAdministrator> Required(Guid id, CancellationToken ct)
    {
        return await db.LocalAdministrators.SingleOrDefaultAsync(x => x.Id == id, ct) ??
               throw new KeyNotFoundException("Local user not found.");
    }

    private async Task EnsureAnotherAdmin(Guid id, CancellationToken ct)
    {
        var users = await db.LocalAdministrators.AsNoTracking().Where(x => x.Id != id && x.Enabled).ToListAsync(ct);
        if (!users.Any(x => UserRoles(x).Contains(AuthorizationPolicies.AdministratorRole)))
            throw new LocalAuthenticationValidationException("The last enabled administrator cannot be changed.");
    }

    private static void ValidateRoles(IReadOnlyList<string> roles)
    {
        if (roles.Count == 0 || roles.Any(role => !Roles.Contains(role, StringComparer.Ordinal)))
            throw new LocalAuthenticationValidationException("Select at least one valid role.");
    }

    private static string TemporaryPassword()
    {
        return
            $"Pg!{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('/', 'x').Replace('+', 'Y')}9";
    }

    private static string ValidateUsername(string username)
    {
        username = (username ?? string.Empty).Trim();
        if (!UsernamePattern().IsMatch(username))
            throw new LocalAuthenticationValidationException(
                "Username must be 3 to 100 characters and use only letters, numbers, periods, underscores, hyphens, or @.");
        return username;
    }

    private static void ValidatePassword(string username, string password)
    {
        if (password is null || password.Length < 12 || password.Length > 128)
            throw new LocalAuthenticationValidationException("Password must be between 12 and 128 characters.");
        if (password.Contains(username, StringComparison.OrdinalIgnoreCase))
            throw new LocalAuthenticationValidationException("Password must not contain the username.");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._@-]{2,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}

public static class LocalAuthenticationDefaults
{
    public const string CookieScheme = "PackageGateway.Local";
}

internal static class ForcedPasswordChangeAccess
{
    public static bool IsAdministrationPageNavigation(HttpRequest request)
    {
        return HttpMethods.IsGet(request.Method) && request.Path.StartsWithSegments("/admin") &&
               request.GetTypedHeaders().Accept?.Any(mediaType =>
                   mediaType.MediaType.Value?.Equals("text/html", StringComparison.OrdinalIgnoreCase) == true) == true;
    }

    public static bool IsAllowed(HttpRequest request)
    {
        return IsAdministrationPageNavigation(request) ||
               request.Path.StartsWithSegments("/admin/auth/status") ||
               request.Path.StartsWithSegments("/admin/auth/change-password") ||
               request.Path.StartsWithSegments("/admin/auth/logout");
    }
}

public sealed record LocalAuthenticationState(bool BootstrapRequired, bool Authenticated, string? Username);

public sealed record LocalCredentialsRequest(string Username, string Password, string? ProviderId = null);

public sealed record LocalPasswordChangeRequest(string CurrentPassword, string NewPassword);

public sealed class LocalBootstrapUnavailableException : Exception;

public sealed class LocalAuthenticationValidationException(string message) : Exception(message);
