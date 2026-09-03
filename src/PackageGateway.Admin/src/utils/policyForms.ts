export const policyTypes = ['VulnerabilityPolicy', 'CooldownPolicy', 'LicensePolicy', 'IntegrityPolicy', 'SignaturePolicy', 'NpmInstallScriptPolicy', 'PackageDenyPolicy', 'PackageAllowPolicy'] as const;
export const policyActions = ['Allow', 'Warn', 'ManualReview', 'Quarantine', 'Block'];
export type PolicyType = typeof policyTypes[number];
export type PolicyFormConfig = Record<string, string | number | string[]>;

export function defaultPolicyConfig(type: PolicyType): PolicyFormConfig {
  switch (type) {
    case 'VulnerabilityPolicy': return { critical: 'Block', high: 'Block', medium: 'Warn', low: 'Allow' };
    case 'CooldownPolicy': return { hours: 72, action: 'ManualReview' };
    case 'LicensePolicy': return { allowed: ['MIT', 'Apache-2.0'], manualReview: ['GPL-3.0'], unknown: 'Warn' };
    case 'IntegrityPolicy': return { mismatch: 'Block', invalidSignature: 'Block', unsigned: 'Warn' };
    case 'SignaturePolicy': return { invalidSignature: 'Block', unsigned: 'Warn' };
    case 'NpmInstallScriptPolicy': return { action: 'ManualReview' };
    default: return { entries: [] };
  }
}

export function parsePolicyConfig(type: PolicyType, json: string): PolicyFormConfig {
  try { return { ...defaultPolicyConfig(type), ...JSON.parse(json) as PolicyFormConfig }; }
  catch { return defaultPolicyConfig(type); }
}
