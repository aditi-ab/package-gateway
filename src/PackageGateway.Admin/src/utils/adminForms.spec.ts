import { describe, expect, it } from 'vitest';
import { formatDateTime } from './dateTime';
import { defaultPolicyConfig, policyTypes } from './policyForms';
import { slugify } from './slug';
import { tokenExpiration } from './tokenExpiration';

describe('admin forms', () => {
  it('formats rendered dates consistently', () => {
    expect(formatDateTime(new Date(2030, 0, 2, 3, 4, 5))).toBe('2030-01-02 03:04:05');
    expect(formatDateTime(null, 'Never')).toBe('Never');
  });
  it('derives stable endpoint slugs', () => expect(slugify('  My Démo Repository  ')).toBe('my-demo-repository'));
  it('provides valid JSON configuration data for every policy handler', () => {
    for (const type of policyTypes)expect(() => JSON.stringify(defaultPolicyConfig(type))).not.toThrow();
  });
  it('maps expiration presets and local custom values to backend timestamps', () => {
    expect(tokenExpiration('90', '', 0)).toBe('1970-04-01T00:00:00.000Z');
    expect(tokenExpiration('never', '', 0)).toBeNull();
    expect(tokenExpiration('custom', '2030-01-01T12:00', 0)).toBe(new Date('2030-01-01T12:00').toISOString());
  });
});
