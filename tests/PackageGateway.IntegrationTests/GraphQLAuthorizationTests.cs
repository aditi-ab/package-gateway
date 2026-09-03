using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PackageGateway.Api;
using Xunit;
using AuthorizeAttribute = HotChocolate.Authorization.AuthorizeAttribute;

namespace PackageGateway.IntegrationTests;

public sealed class GraphQLAuthorizationTests
{
    [Theory]
    [InlineData(AuthorizationPolicies.ReaderRole, AuthorizationPolicies.Reader, true)]
    [InlineData(AuthorizationPolicies.ReaderRole, AuthorizationPolicies.SecurityReviewer, false)]
    [InlineData(AuthorizationPolicies.SecurityReviewerRole, AuthorizationPolicies.Reader, true)]
    [InlineData(AuthorizationPolicies.SecurityReviewerRole, AuthorizationPolicies.SecurityReviewer, true)]
    [InlineData(AuthorizationPolicies.RepositoryAdminRole, AuthorizationPolicies.Reader, true)]
    [InlineData(AuthorizationPolicies.RepositoryAdminRole, AuthorizationPolicies.RepositoryAdmin, true)]
    [InlineData(AuthorizationPolicies.AdministratorRole, AuthorizationPolicies.Reader, true)]
    [InlineData(AuthorizationPolicies.AdministratorRole, AuthorizationPolicies.SecurityReviewer, true)]
    [InlineData(AuthorizationPolicies.AdministratorRole, AuthorizationPolicies.RepositoryAdmin, true)]
    [InlineData(AuthorizationPolicies.AdministratorRole, AuthorizationPolicies.Administrator, true)]
    public async Task Role_hierarchy_enforces_each_management_policy(string role, string policy, bool expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(AuthorizationPolicies.Configure);
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal =
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test"), new Claim(ClaimTypes.Role, role)], "test"));
        Assert.Equal(expected, (await authorization.AuthorizeAsync(principal, null, policy)).Succeeded);
    }

    [Fact]
    public void Every_graphql_operation_declares_an_authorization_policy()
    {
        AssertPolicies<Query>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(Query.GetAccessTokens)] = AuthorizationPolicies.Administrator,
            [nameof(Query.GetLocalUsers)] = AuthorizationPolicies.Administrator,
            [nameof(Query.GetLocalRoleCatalog)] = AuthorizationPolicies.Administrator,
            [nameof(Query.GetEntraConnection)] = AuthorizationPolicies.Administrator
        }, AuthorizationPolicies.Reader);
        AssertPolicies<Mutation>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(Mutation.ApprovePackageVersion)] = AuthorizationPolicies.SecurityReviewer,
            [nameof(Mutation.WaivePackageVersion)] = AuthorizationPolicies.SecurityReviewer,
            [nameof(Mutation.BlockPackageVersion)] = AuthorizationPolicies.SecurityReviewer,
            [nameof(Mutation.QuarantinePackageVersion)] = AuthorizationPolicies.SecurityReviewer,
            [nameof(Mutation.RequirePackageVersionReview)] = AuthorizationPolicies.SecurityReviewer,
            [nameof(Mutation.RescanPackageVersion)] = AuthorizationPolicies.SecurityReviewer,
            [nameof(Mutation.RemovePackageVersion)] = AuthorizationPolicies.Administrator,
            [nameof(Mutation.CreateAccessToken)] = AuthorizationPolicies.Administrator,
            [nameof(Mutation.RevokeAccessToken)] = AuthorizationPolicies.Administrator,
            [nameof(Mutation.CreateLocalUser)] = AuthorizationPolicies.Administrator,
            [nameof(Mutation.UpdateLocalUser)] = AuthorizationPolicies.Administrator,
            [nameof(Mutation.ResetLocalUserPassword)] = AuthorizationPolicies.Administrator,
            [nameof(Mutation.DeleteLocalUser)] = AuthorizationPolicies.Administrator,
            [nameof(Mutation.UpdateEntraConnection)] = AuthorizationPolicies.Administrator
        }, AuthorizationPolicies.RepositoryAdmin);
    }

    private static void AssertPolicies<T>(IReadOnlyDictionary<string, string> overrides, string defaultPolicy)
    {
        foreach (var method in typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                    BindingFlags.DeclaredOnly))
        {
            var attribute = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(overrides.GetValueOrDefault(method.Name, defaultPolicy), attribute.Policy);
        }
    }
}