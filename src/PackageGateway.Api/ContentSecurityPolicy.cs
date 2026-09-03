namespace PackageGateway.Api;

internal static class ContentSecurityPolicy
{
    internal static string Resolve(PathString requestPath)
    {
        var scriptSource = requestPath.StartsWithSegments("/docs")
            ? "'self' 'unsafe-inline'"
            : "'self'";

        return
            $"default-src 'self'; script-src {scriptSource}; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self' https://login.microsoftonline.com https://*.microsoftonline.com; frame-src 'self' https://login.microsoftonline.com https://*.microsoftonline.com";
    }
}