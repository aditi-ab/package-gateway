import { describe, expect, it } from 'vitest';
import { addPackageVersionMutation, upstreamPackageSearchQuery, upstreamPackageVersionsQuery } from './upstreamPackages';

describe('upstream package operations', () => {
  it('searches upstreams without invoking acquisition', () => {
    expect(upstreamPackageSearchQuery).toContain('upstreamPackages');
    expect(upstreamPackageSearchQuery).not.toContain('addPackageVersion');
  });

  it('adds an exact package version through the management mutation', () => {
    expect(addPackageVersionMutation).toContain('addPackageVersion');
    expect(addPackageVersionMutation).toContain('$version: String!');
    expect(addPackageVersionMutation).toContain('errors');
  });

  it('loads versions for the selected upstream package', () => {
    expect(upstreamPackageVersionsQuery).toContain('upstreamPackageVersions');
    expect(upstreamPackageVersionsQuery).toContain('$upstreamId: UUID!');
    expect(upstreamPackageVersionsQuery).toContain('$packageName: String!');
  });
});
