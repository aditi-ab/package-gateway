using System.Text.Json;
using PackageGateway.Application;
using PackageGateway.Domain;

namespace PackageGateway.Security;

public sealed class PolicyEvaluator(IEnumerable<IPackagePolicyRule> customRules) : IPackagePolicyEvaluator
{
    public async Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<RuleEvaluation>();
        foreach (var finding in context.Inspection.Findings.Where(x => x.IsHardBlock))
            results.Add(new RuleEvaluation(finding.Type, PolicyAction.Block, finding.Description, IsHardBlock: true));
        foreach (var policy in context.Policies.Where(x => x.Enabled && x.AppliesTo(context.Package.PackageType)))
        {
            var custom = customRules.FirstOrDefault(x => x.Type == policy.Type);
            if (custom is not null)
            {
                results.Add(await custom.EvaluateAsync(context, policy, cancellationToken));
                continue;
            }

            results.Add(EvaluateBuiltIn(context, policy));
        }

        if (context.Inspection.RiskScore >= 100)
            results.Add(new RuleEvaluation("RiskScore", PolicyAction.Block, "Risk score is 100 or greater."));
        else if (context.Inspection.RiskScore >= 70)
            results.Add(new RuleEvaluation("RiskScore", PolicyAction.Quarantine, "Risk score is between 70 and 99."));
        else if (context.Inspection.RiskScore >= 40)
            results.Add(new RuleEvaluation("RiskScore", PolicyAction.ManualReview, "Risk score is between 40 and 69."));
        if (results.Count == 0)
            results.Add(new RuleEvaluation("Default", PolicyAction.Allow, "No policy denied the artifact."));
        var final = results.MaxBy(x => Rank(x.Action))!.Action;
        return new PolicyEvaluation(final, context.Inspection.RiskScore, results.Any(x => x.IsHardBlock), results);
    }

    private static RuleEvaluation EvaluateBuiltIn(PolicyEvaluationContext context, Policy policy)
    {
        using var json = JsonDocument.Parse(policy.ConfigJson);
        var config = json.RootElement;
        return policy.Type switch
        {
            "VulnerabilityPolicy" => Vulnerabilities(context, policy, config),
            "CooldownPolicy" => Cooldown(context, policy, config),
            "LicensePolicy" => License(context, policy, config),
            "IntegrityPolicy" => Integrity(context, policy, config, true),
            "SignaturePolicy" => Integrity(context, policy, config, false),
            "NpmInstallScriptPolicy" => InstallScripts(context, policy, config),
            "PackageDenyPolicy" => PackageList(context, policy, config, true),
            "PackageAllowPolicy" => PackageList(context, policy, config, false),
            _ => new RuleEvaluation(policy.Type, PolicyAction.Warn, $"Unknown policy type '{policy.Type}'.", policy.Id)
        };
    }

    private static RuleEvaluation Vulnerabilities(PolicyEvaluationContext context, Policy policy, JsonElement config)
    {
        var highest = context.Vulnerabilities.OrderByDescending(x => x.Severity).FirstOrDefault();
        if (highest is null)
            return new RuleEvaluation(policy.Type, PolicyAction.Allow, "No known vulnerabilities were reported.",
                policy.Id);
        var key = highest.Severity.ToString().ToLowerInvariant();
        var action =
            config.TryGetProperty(key, out var configured) &&
            Enum.TryParse<PolicyAction>(configured.GetString(), true, out var parsed)
                ? parsed
                : PolicyAction.ManualReview;
        return new RuleEvaluation(policy.Type, action,
            $"Highest vulnerability severity is {highest.Severity} ({highest.ExternalId}).", policy.Id);
    }

    private static RuleEvaluation Cooldown(PolicyEvaluationContext context, Policy policy, JsonElement config)
    {
        if (context.Version.PublishedAt is null)
            return new RuleEvaluation(policy.Type, PolicyAction.ManualReview, "Publication time is unknown.",
                policy.Id);
        var hours = config.GetProperty("hours").GetDouble();
        var age = context.EvaluatedAt - context.Version.PublishedAt.Value;
        if (age >= TimeSpan.FromHours(hours))
            return new RuleEvaluation(policy.Type, PolicyAction.Allow,
                $"Package is older than the {hours:g}-hour cooldown.", policy.Id);
        var action = Enum.Parse<PolicyAction>(config.GetProperty("action").GetString()!, true);
        return new RuleEvaluation(policy.Type, action, $"Package is inside the {hours:g}-hour cooldown.", policy.Id);
    }

    private static RuleEvaluation License(PolicyEvaluationContext context, Policy policy, JsonElement config)
    {
        var license = context.Inspection.License;
        if (string.IsNullOrWhiteSpace(license))
            return new RuleEvaluation(policy.Type,
                ParseAction(config.GetProperty("unknown").GetString(), PolicyAction.Warn),
                "Package license is unknown.", policy.Id);
        if (Contains(config, "allowed", license))
            return new RuleEvaluation(policy.Type, PolicyAction.Allow, $"License {license} is allowed.", policy.Id);
        if (Contains(config, "manualReview", license))
            return new RuleEvaluation(policy.Type, PolicyAction.ManualReview, $"License {license} requires review.",
                policy.Id);
        return new RuleEvaluation(policy.Type, PolicyAction.Warn, $"License {license} is not explicitly classified.",
            policy.Id);
    }

    private static RuleEvaluation Integrity(PolicyEvaluationContext context, Policy policy, JsonElement config,
        bool checkContentIntegrity)
    {
        if (checkContentIntegrity && context.Inspection.Findings.FirstOrDefault(x => x.IsHardBlock) is not null)
        {
            var action = ConfiguredAction(config, "mismatch", PolicyAction.Block);
            return new RuleEvaluation(policy.Type, action, "Artifact integrity validation failed.", policy.Id,
                action == PolicyAction.Block);
        }

        if (context.Inspection.SignatureStatus == SignatureStatus.Invalid)
        {
            var action = ConfiguredAction(config, "invalidSignature", PolicyAction.Block);
            return new RuleEvaluation(policy.Type, action, "Package signature validation failed.", policy.Id,
                action == PolicyAction.Block);
        }

        if (context.Inspection.SignatureStatus == SignatureStatus.Unsigned)
        {
            var action = ConfiguredAction(config, "unsigned", PolicyAction.Warn);
            return new RuleEvaluation(policy.Type, action, "NuGet package is unsigned.", policy.Id);
        }

        return new RuleEvaluation(policy.Type, PolicyAction.Allow, "No integrity failure was found.", policy.Id);
    }

    private static RuleEvaluation InstallScripts(PolicyEvaluationContext context, Policy policy, JsonElement config)
    {
        if (!context.Inspection.HasInstallScripts)
            return new RuleEvaluation(policy.Type, PolicyAction.Allow,
                "No npm install-time lifecycle script was found.", policy.Id);
        return new RuleEvaluation(policy.Type,
            ParseAction(config.GetProperty("action").GetString(), PolicyAction.ManualReview),
            "Package defines an npm install-time lifecycle script.", policy.Id);
    }

    private static RuleEvaluation PackageList(PolicyEvaluationContext context, Policy policy, JsonElement config,
        bool deny)
    {
        var matched = config.TryGetProperty("entries", out var entries) && entries.EnumerateArray()
            .Any(x => Matches(x.GetString(), context.Package.NormalizedName, context.Version.Version));
        if (!matched) return new RuleEvaluation(policy.Type, PolicyAction.Allow, "No package rule matched.", policy.Id);
        return new RuleEvaluation(policy.Type, deny ? PolicyAction.Block : PolicyAction.Allow,
            $"Package matched an explicit {(deny ? "deny" : "allow")} rule.", policy.Id);
    }

    private static bool Matches(string? pattern, string name, string version)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var separator = pattern.StartsWith('@')
            ? pattern.IndexOf('@', pattern.IndexOf('/') + 1)
            : pattern.LastIndexOf('@');
        var namePattern = (separator > 0 ? pattern[..separator] : pattern).ToLowerInvariant();
        var versionPattern = separator > 0 ? pattern[(separator + 1)..] : null;
        var nameMatch = namePattern.EndsWith('*')
            ? name.StartsWith(namePattern[..^1], StringComparison.Ordinal)
            : name == namePattern;
        return nameMatch && (versionPattern is null || versionPattern == version);
    }

    private static bool Contains(JsonElement config, string property, string value)
    {
        return config.TryGetProperty(property, out var array) && array.EnumerateArray()
            .Any(x => string.Equals(x.GetString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static PolicyAction ConfiguredAction(JsonElement config, string property, PolicyAction fallback)
    {
        return config.TryGetProperty(property, out var value) ? ParseAction(value.GetString(), fallback) : fallback;
    }

    private static PolicyAction ParseAction(string? value, PolicyAction fallback)
    {
        return Enum.TryParse<PolicyAction>(value, true, out var parsed) ? parsed : fallback;
    }

    private static int Rank(PolicyAction action)
    {
        return action switch
        {
            PolicyAction.Allow => 0, PolicyAction.Warn => 1, PolicyAction.ManualReview => 2,
            PolicyAction.Quarantine => 3, PolicyAction.Block => 4, _ => 4
        };
    }
}