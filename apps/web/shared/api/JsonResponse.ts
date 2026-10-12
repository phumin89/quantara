import { z } from 'zod';

export class JsonResponse {
  public static async read(response: Response): Promise<unknown> {
    const text = await response.text();

    return text ? (JSON.parse(text) as unknown) : {};
  }

  public static error(body: unknown, fallback: string): string {
    const parsed = z.object({ error: z.string() }).safeParse(body);

    return parsed.success ? parsed.data.error : fallback;
  }
}
