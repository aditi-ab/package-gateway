namespace PackageGateway.Api;

public sealed class AuthenticationSettings
{
    public const string SectionName = "Authentication";
    public string Mode { get; set; } = "Local";
    public string? Authority { get; set; }
    public string? Audience { get; set; }
    public string? ClientId { get; set; }
    public string? ManagementScope { get; set; }
    public string? DataProtectionKeysPath { get; set; }
    public string DocumentationUrl { get; set; } = "/docs/";

    public bool IsLocal => Mode.Equals("Local", StringComparison.OrdinalIgnoreCase);
    public bool IsEntra => Mode.Equals("Entra", StringComparison.OrdinalIgnoreCase);
    public bool IsLocalAndEntra => Mode.Equals("LocalAndEntra", StringComparison.OrdinalIgnoreCase);
    public bool LocalEnabled => IsLocal || IsLocalAndEntra;
    public bool EntraEnabled => IsEntra || IsLocalAndEntra;
}