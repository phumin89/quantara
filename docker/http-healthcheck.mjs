const url = process.env.HEALTHCHECK_URL ?? 'http://127.0.0.1:3000/';
const timeout = setTimeout(() => process.exit(1), 2000);

try {
  const response = await fetch(url, { signal: AbortSignal.timeout(1500) });
  clearTimeout(timeout);
  if (!response.ok) process.exit(1);
} catch {
  clearTimeout(timeout);
  process.exit(1);
}
