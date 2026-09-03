export function tokenExpiration(value: string, customValue: string, now = Date.now()): string | null {
  if (value === 'never')
    return null;

  if (value === 'custom')
    return customValue ? new Date(customValue).toISOString() : null;

  return new Date(now + Number(value) * 86400000).toISOString();
}
