export const removePackageVersionMutation = `
  mutation ($id: UUID!, $reason: String!) {
    removePackageVersion(id: $id, reason: $reason) {
      errors {
        code
        message
      }
    }
  }
`;
