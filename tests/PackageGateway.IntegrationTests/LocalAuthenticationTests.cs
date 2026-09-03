using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PackageGateway.Api;
using PackageGateway.Storage;
using Xunit;

namespace PackageGateway.IntegrationTests;

public sealed class LocalAuthenticationTests
{
    [Theory]
    [InlineData("GET", "/admin/", false)]
    [InlineData("GET", "/admin/users", false)]
    [InlineData("HEAD", "/admin/assets/application.js", false)]
    [InlineData("POST", "/admin/auth/change-password", true)]
    [InlineData("POST", "/admin/auth/logout", true)]
    [InlineData("GET", "/graphql", false)]
    [InlineData("POST", "/api/admin/users", false)]
    [InlineData("POST", "/admin/", false)]
    public void Forced_password_change_allows_only_password_session_and_html_page_requests(string method, string path,
        bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        Assert.Equal(expected, ForcedPasswordChangeAccess.IsAllowed(context.Request));
    }

    [Theory]
    [InlineData("/admin/")]
    [InlineData("/admin/users")]
    public void Forced_password_change_allows_administration_page_navigation(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.Headers.Accept = "text/html";

        Assert.True(ForcedPasswordChangeAccess.IsAllowed(context.Request));
    }

    [Theory]
    [InlineData("GET", "/admin/", "text/html", true)]
    [InlineData("GET", "/admin/users", "text/html,application/xhtml+xml", true)]
    [InlineData("GET", "/admin/config.json", "application/json", false)]
    [InlineData("GET", "/graphql", "text/html", false)]
    [InlineData("POST", "/admin/", "text/html", false)]
    public void Administration_page_navigation_is_detected(string method, string path,
        string accept, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;

        Assert.Equal(expected, ForcedPasswordChangeAccess.IsAdministrationPageNavigation(context.Request));
    }

    [Fact]
    public async Task First_administrator_can_bootstrap_once_and_log_in()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var authentication = new LocalAuthenticationService(db, new PasswordHasher<string>());

        var initial = await authentication.GetStateAsync(new ClaimsPrincipal(), ct);
        Assert.True(initial.BootstrapRequired);
        Assert.False(initial.Authenticated);

        var administrator = await authentication.BootstrapAsync("gateway.admin", "A long local password 42!", ct);
        Assert.NotEqual("A long local password 42!", administrator.PasswordHash);
        Assert.True(LocalAuthenticationService.CreatePrincipal(administrator)
            .IsInRole(AuthorizationPolicies.AdministratorRole));
        Assert.Equal(administrator.Id,
            (await authentication.ValidateCredentialsAsync("GATEWAY.ADMIN", "A long local password 42!", ct))?.Id);
        Assert.Null(await authentication.ValidateCredentialsAsync("gateway.admin", "incorrect password", ct));
        await Assert.ThrowsAsync<LocalBootstrapUnavailableException>(() =>
            authentication.BootstrapAsync("another.admin", "Another long password 42!", ct));

        var reader = await authentication.CreateAsync("package.reader", [AuthorizationPolicies.ReaderRole],
            administrator.Username, ct);
        Assert.True(reader.User.MustChangePassword);
        Assert.True(LocalAuthenticationService.CreatePrincipal(reader.User)
            .IsInRole(AuthorizationPolicies.ReaderRole));
        Assert.NotNull(await authentication.ValidateCredentialsAsync("PACKAGE.READER", reader.TemporaryPassword, ct));

        var audit = await db.AuditEvents.SingleAsync(x => x.Action == "LocalAdministratorBootstrapped", ct);
        Assert.Equal("LocalAdministratorBootstrapped", audit.Action);
    }

    [Theory]
    [InlineData("ab", "A sufficiently long password 42!", "Username")]
    [InlineData("valid-admin", "too-short", "Password")]
    [InlineData("valid-admin", "This password contains valid-admin", "username")]
    public async Task Bootstrap_rejects_invalid_credentials(string username, string password, string expectedMessage)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var authentication = new LocalAuthenticationService(db, new PasswordHasher<string>());

        var exception =
            await Assert.ThrowsAsync<LocalAuthenticationValidationException>(() =>
                authentication.BootstrapAsync(username, password, ct));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.LocalAdministrators);
    }
}
