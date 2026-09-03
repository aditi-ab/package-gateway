export function formatDateTime(value: string | Date | null | undefined, fallback = 'Not available'): string {
  if (!value)
    return fallback;

  const date = value instanceof Date ? value : new Date(value);

  if (Number.isNaN(date.getTime()))
    return fallback;

  const pad = (part: number) => String(part).padStart(2, '0');

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}
