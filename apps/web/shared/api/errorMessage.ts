import { z } from 'zod';

export default function errorMessage(body: unknown, fallback: string): string {
  const parsed = z.object({ error: z.string() }).safeParse(body);

  return parsed.success ? parsed.data.error : fallback;
}
