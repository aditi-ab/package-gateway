using System.Security.Claims;
using System.Threading.RateLimiting;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Aditify.Identity;
using PackageGateway.Api;
using PackageGateway.Application;
using PackageGateway.Infrastructure;
using PackageGateway.Protocols.Npm;
using PackageGateway.Protocols.NuGet;
using PackageGateway.Security;
using PackageGateway.Storage;

public class Program
{
    public static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var authenticationMode = builder.Configuration["Authentication:Mode"] ?? "Local";
        const bool useLocalAuthentication = true;
        var useEntraAuthentication = authenticationMode.Equals("Entra", StringComparison.OrdinalIgnoreCase) ||
                                     authenticationMode.Equals("LocalAndEntra", StringComparison.OrdinalIgnoreCase);
        var entraConnectionState = new EntraConnectionState(new EntraConnectionSnapshot(useEntraAuthentication,
            builder.Configuration["Authentication:Authority"] ?? string.Empty,
            builder.Configuration["Authentication:Audience"] ?? string.Empty,
            builder.Configuration["Authentication:ClientId"] ?? string.Empty,
            builder.Configuration["Authentication:ManagementScope"] ?? string.Empty, Guid.Empty));
        builder.Services.AddSingleton(entraConnectionState);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<GatewayManagementService>();
        builder.Services.AddScoped<IUpstreamResolver, UpstreamResolver>();
        builder.Services.AddScoped<IUpstreamPackageSearch, UpstreamPackageSearch>();
        builder.Services.AddOptions<AuthenticationSettings>()
            .Bind(builder.Configuration.GetSection(AuthenticationSettings.SectionName))
            .Validate(x => x.IsLocal || x.IsEntra || x.IsLocalAndEntra,
                "Authentication:Mode must be Local, Entra, or LocalAndEntra.")
            .Validate(
                x => !x.EntraEnabled || (Uri.TryCreate(x.Authority, UriKind.Absolute, out var authority) &&
                                         authority.Scheme == Uri.UriSchemeHttps),
                "Authentication:Authority must be an absolute HTTPS URL in Entra mode.")
            .Validate(x => !x.EntraEnabled || !string.IsNullOrWhiteSpace(x.Audience),
                "Authentication:Audience is required in Entra mode.")
            .Validate(x => !x.EntraEnabled || !string.IsNullOrWhiteSpace(x.ClientId),
                "Authentication:ClientId is required in Entra mode.")
            .Validate(x => !x.EntraEnabled || !string.IsNullOrWhiteSpace(x.ManagementScope),
                "Authentication:ManagementScope is required in Entra mode.").ValidateOnStart();
        builder.Services.AddGatewayStorage(builder.Configuration).AddGatewaySecurity(builder.Configuration)
            .AddGatewayInfrastructure(builder.Configuration).AddNuGetProtocol().AddNpmProtocol();
        builder.Services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
        builder.Services.AddScoped<LocalAuthenticationService>();
        builder.Services.AddScoped<EntraConnectionService>();
        var authentication = builder.Services.AddAuthentication("PackageGateway.Authentication")
            .AddPolicyScheme("PackageGateway.Authentication", "PackageGateway.Authentication", options =>
                options.ForwardDefaultSelector = context => entraConnectionState.Current.Configured &&
                                                            context.Request.Headers.Authorization.ToString()
                                                                .StartsWith("Bearer ",
                                                                    StringComparison.OrdinalIgnoreCase)
                    ? JwtBearerDefaults.AuthenticationScheme
                    : LocalAuthenticationDefaults.CookieScheme);
        if (useLocalAuthentication)
        {
            var dataProtectionKeysPath = builder.Configuration["Authentication:DataProtectionKeysPath"];
            var cookieSecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            var cookieNamePrefix = builder.Environment.IsDevelopment() ? "PackageGateway" : "__Host-PackageGateway";
            var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PackageGateway");
            if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
                dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
            builder.Services.AddAntiforgery(options =>
            {
                options.Cookie.Name = $"{cookieNamePrefix}.Antiforgery";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = cookieSecurePolicy;
                options.HeaderName = "X-CSRF-TOKEN";
            });
            authentication.AddCookie(
                LocalAuthenticationDefaults.CookieScheme, options =>
                {
                    options.Cookie.Name = $"{cookieNamePrefix}.Auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = cookieSecurePolicy;
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                    options.SlidingExpiration = true;
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        var local = context.HttpContext.RequestServices
                            .GetRequiredService<LocalAuthenticationService>();
                        if (!await local.ValidatePrincipalAsync(context.Principal!, context.HttpContext.RequestAborted))
                            context.RejectPrincipal();
                    };
                });
        }

        authentication.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = builder.Configuration["Authentication:Authority"];
            options.Audience = builder.Configuration["Authentication:Audience"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, RoleClaimType = "roles",
                NameClaimType = "preferred_username"
            };
        });
        builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, DynamicEntraJwtOptions>();
        builder.Services.AddAditifyIdentity(options =>
        {
            options.BasePath = "/admin";
            options.CookieScheme = LocalAuthenticationDefaults.CookieScheme;
            options.RegisterCookieScheme = false;
            options.AdministratorPolicy = AuthorizationPolicies.Administrator;
            options.AdministratorRole = AuthorizationPolicies.AdministratorRole;
            options.SecurityStampClaim = "packagegateway.security-stamp";
            options.MustChangePasswordClaim = LocalAuthenticationService.MustChangePasswordClaim;
        });
        builder.Services.AddScoped<IAdminIdentityStore, GatewayAdminIdentityStore>();
        builder.Services.AddSingleton<IProductRoleCatalog, GatewayRoleCatalog>();
        builder.Services.AddScoped<IAdminIdentityAuditSink, GatewayIdentityAuditSink>();

        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("management",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("authentication",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                        { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("package",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                        { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
        builder.Services.AddGraphQLServer().AddAuthorization().AddQueryType<Query>().AddMutationType<Mutation>()
            .AddTypeExtension<PackageVersionGraphQL>().AddTypeExtension<RepositoryGraphQL>()
            .AddCostAnalyzer().ModifyCostOptions(options =>
            {
                options.MaxFieldCost = 1_000;
                options.MaxTypeCost = 1_000;
                options.EnforceCostLimits = true;
            })
            .ModifyPagingOptions(options =>
            {
                options.DefaultPageSize = 25;
                options.MaxPageSize = 100;
                options.IncludeTotalCount = true;
            })
            .ModifyServerOptions(options =>
            {
                options.Tool.Enable = builder.Environment.IsDevelopment();
                options.EnableSchemaRequests = builder.Environment.IsDevelopment();
                options.MaxBatchSize = 10;
                options.MaxConcurrentExecutions = 64;
            });
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);
        builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("PackageGateway"))
            .WithTracing(tracing => tracing.AddSource(GatewayDiagnostics.ActivitySourceName)
                .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter())
            .WithMetrics(metrics => metrics.AddMeter(GatewayDiagnostics.MeterName).AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddOtlpExporter());
        var app = builder.Build();
        if (args.Length == 2 && args[0].Equals("database", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("migrate", StringComparison.OrdinalIgnoreCase))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IGatewayStore>().MigrateAsync(CancellationToken.None);
            return;
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EntraConnectionService>().LoadAsync(CancellationToken.None);
        }

        _ = await app.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
        app.UseForwardedHeaders(new ForwardedHeadersOptions
            { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler(exception => exception.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await Results.Problem("The request failed unexpectedly.", statusCode: 500).ExecuteAsync(context);
            }));
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] =
                    ContentSecurityPolicy.Resolve(context.Request.Path);
                return Task.CompletedTask;
            });
            await next(context);
        });
        app.UseDefaultFiles();
        app.Use(async (context, next) =>
        {
            context.Request.Path = DocumentationPathRewriter.Resolve(context.Request.Path,
                path => app.Environment.WebRootFileProvider.GetFileInfo(path).Exists);
            await next(context);
        });
        app.UseStaticFiles();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            var forced = context.User.Identity?.AuthenticationType == LocalAuthenticationDefaults.CookieScheme &&
                         context.User.HasClaim(LocalAuthenticationService.MustChangePasswordClaim, "true");
            var allowed = ForcedPasswordChangeAccess.IsAllowed(context.Request);
            if (forced && !allowed)
            {
                context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
                return;
            }

            await next(context);
        });
        if (useLocalAuthentication)
        {
            app.UseAntiforgery();
            app.Use(async (context, next) =>
            {
                if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/graphql" &&
                    context.User.Identity?.AuthenticationType == LocalAuthenticationDefaults.CookieScheme)
                    await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
                await next(context);
            });
        }

        app.MapGet("/admin/config.json",
            (IOptions<AuthenticationSettings> settings, EntraConnectionState entraState) =>
            {
                var entra = entraState.Current;
                return Results.Json(new
                {
                    authenticationMode = entra.Configured ? "localandentra" : "local",
                    authority = entra.Authority, clientId = entra.ClientId,
                    scopes = entra.Configured ? new[] { entra.Scope } : Array.Empty<string>(),
                    graphqlEndpoint = "/graphql", documentationUrl = settings.Value.DocumentationUrl
                });
            }).AllowAnonymous();
        if (useLocalAuthentication)
        {
            MapLocalAuthentication(app);
            app.MapAditifyIdentityExternalAuthentication();
        }
        app.MapAditifyIdentityManagement();
        app.MapGet("/", () => Results.Redirect("/admin/")).AllowAnonymous();
        app.MapHealthChecks("/health/live",
            new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready",
            new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapGraphQL().RequireRateLimiting("management").RequireAuthorization();
        app.MapNuGetProtocol();
        app.MapNpmProtocol();
        app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html").AllowAnonymous();
        await app.RunAsync();
    }

    private static void MapLocalAuthentication(WebApplication app)
    {
        var group = app.MapGroup("/admin/auth").AllowAnonymous();
        group.MapGet("/status",
            async (HttpContext context, LocalAuthenticationService authentication, IAdminIdentityStore identities, IAntiforgery antiforgery,
                CancellationToken cancellationToken) =>
            {
                var state = await authentication.GetStateAsync(context.User, cancellationToken);
                var token = antiforgery.GetAndStoreTokens(context).RequestToken;
                return Results.Json(new
                {
                    state.BootstrapRequired, state.Authenticated, state.Username,
                    mustChangePassword =
                        context.User.HasClaim(LocalAuthenticationService.MustChangePasswordClaim, "true"),
                    providers = (await identities.ListProvidersAsync(cancellationToken)).Where(x => x.Enabled)
                        .Select(x => new { x.Id, x.DisplayName, type = x.Type.ToString().ToLowerInvariant() }),
                    antiforgeryToken = token
                });
            });
        group.MapPost("/bootstrap",
            async (LocalCredentialsRequest request, HttpContext context, LocalAuthenticationService authentication,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var administrator =
                        await authentication.BootstrapAsync(request.Username, request.Password, cancellationToken);
                    await context.SignInAsync(LocalAuthenticationDefaults.CookieScheme,
                        LocalAuthenticationService.CreatePrincipal(administrator));
                    return Results.NoContent();
                }
                catch (LocalAuthenticationValidationException exception)
                {
                    return Results.BadRequest(new { message = exception.Message });
                }
                catch (LocalBootstrapUnavailableException)
                {
                    return Results.Conflict(new { message = "Local administration has already been initialized." });
                }
            }).RequireRateLimiting("authentication").WithMetadata(new RequireAntiforgeryTokenAttribute());
        group.MapPost("/login",
            async (LocalCredentialsRequest request, HttpContext context, LocalAuthenticationService authentication,
                AdminIdentityService identity, IExternalIdentityService external, CancellationToken cancellationToken) =>
            {
                if (!string.IsNullOrWhiteSpace(request.ProviderId))
                {
                    try
                    {
                        var externalUser = await identity.PasswordSignInAsync(request.Username, request.Password,
                            request.ProviderId, external, cancellationToken);
                        await identity.SignInAsync(context, externalUser);
                        return Results.NoContent();
                    }
                    catch (IdentityOperationException exception)
                    {
                        return Results.Json(new { code = exception.Code, message = exception.Message },
                            statusCode: exception.StatusCode);
                    }
                }
                var administrator =
                    await authentication.ValidateCredentialsAsync(request.Username, request.Password,
                        cancellationToken);
                if (administrator is null)
                    return Results.Json(new { message = "The username or password is incorrect." },
                        statusCode: StatusCodes.Status401Unauthorized);
                await context.SignInAsync(LocalAuthenticationDefaults.CookieScheme,
                    LocalAuthenticationService.CreatePrincipal(administrator));
                return Results.NoContent();
            }).RequireRateLimiting("authentication").WithMetadata(new RequireAntiforgeryTokenAttribute());
        group.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(LocalAuthenticationDefaults.CookieScheme);
            return Results.NoContent();
        }).WithMetadata(new RequireAntiforgeryTokenAttribute());
        group.MapPost("/change-password",
            async (LocalPasswordChangeRequest request, HttpContext context, LocalAuthenticationService authentication,
                CancellationToken ct) =>
            {
                var claim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(claim, out var id)) return Results.Unauthorized();
                try
                {
                    await authentication.ChangePasswordAsync(id, request.CurrentPassword, request.NewPassword,
                        context.User.Identity!.Name!, ct);
                }
                catch (LocalAuthenticationValidationException exception)
                {
                    return Results.BadRequest(new { message = exception.Message });
                }

                var user = (await authentication.ListAsync(ct)).Single(x => x.Id == id);
                await context.SignInAsync(LocalAuthenticationDefaults.CookieScheme,
                    LocalAuthenticationService.CreatePrincipal(user));
                return Results.NoContent();
            }).RequireAuthorization().WithMetadata(new RequireAntiforgeryTokenAttribute());
    }
}
