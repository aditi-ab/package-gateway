export const upstreamPackageSearchQuery = `
  query UpstreamPackages(
    $repositoryId: UUID!
    $packageType: PackageType!
    $search: String!
  ) {
    upstreamPackages(
      repositoryId: $repositoryId
      packageType: $packageType
      search: $search
      first: 25
    ) {
      upstreamId
      upstreamName
      packageType
      name
      version
      description
    }
  }
`;

export const addPackageVersionMutation = `
  mutation AddPackageVersion(
    $repositoryId: UUID!
    $packageType: PackageType!
    $packageName: String!
    $version: String!
  ) {
    addPackageVersion(
      repositoryId: $repositoryId
      packageType: $packageType
      packageName: $packageName
      version: $version
    ) {
      packageVersion {
        version
        status
      }
      errors {
        code
        message
      }
    }
  }
`;

export const upstreamPackageVersionsQuery = `
  query UpstreamPackageVersions(
    $repositoryId: UUID!
    $upstreamId: UUID!
    $packageType: PackageType!
    $packageName: String!
  ) {
    upstreamPackageVersions(
      repositoryId: $repositoryId
      upstreamId: $upstreamId
      packageType: $packageType
      packageName: $packageName
    )
  }
`;
