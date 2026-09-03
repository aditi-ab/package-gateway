using Microsoft.AspNetCore.Authorization;

namespace PackageGateway.Api;

public static class AuthorizationPolicies
{
    public const string Reader = "PackageGateway.Reader";
    public const string SecurityReviewer = "PackageGateway.SecurityReviewer";
    public const string RepositoryAdmin = "PackageGateway.RepositoryAdmin";
    public const string Administrator = "PackageGateway.Administrator";
    public const string ReaderRole = "Reader";
    public const string SecurityReviewerRole = "SecurityReviewer";
    public const string RepositoryAdminRole = "RepositoryAdmin";
    public const string AdministratorRole = "Administrator";

    public static void Configure(AuthorizationOptions options)
    {
        Add(options, Reader, ReaderRole, SecurityReviewerRole, RepositoryAdminRole, AdministratorRole);
        Add(options, SecurityReviewer, SecurityReviewerRole, AdministratorRole);
        Add(options, RepositoryAdmin, RepositoryAdminRole, AdministratorRole);
        Add(options, Administrator, AdministratorRole);
    }

    private static void Add(AuthorizationOptions options, string name, params string[] roles)
    {
        options.AddPolicy(name, policy => policy.RequireAuthenticatedUser().RequireRole(roles));
    }
}