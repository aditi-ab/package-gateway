import { describe, expect, it } from 'vitest';
import { removePackageVersionMutation } from './packageVersionMutations';

describe('package version mutations', () => {
  it('requests fields exposed by BooleanPayload', () => {
    expect(removePackageVersionMutation).toContain('errors');
    expect(removePackageVersionMutation).not.toMatch(/\bvalue\b/);
  });
});
