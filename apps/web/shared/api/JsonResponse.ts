import { z } from 'zod';

export class JsonResponse {
  public static readonly uncertainWrite =
    'The action may have completed. Refresh status before taking another action.';

  public static async read(
    response: Response,
    fallback = 'Request failed.',
  ): Promise<unknown> {
    const text = await response.text();

    if (!text) {
      return {};
    }

    try {
      return JSON.parse(text) as unknown;
    } catch (error) {
      if (error instanceof SyntaxError) {
        throw new Error(fallback);
      }

      throw error;
    }
  }

  public static error(body: unknown, fallback: string): string {
    const parsed = z.object({ error: z.string() }).safeParse(body);

    return parsed.success && parsed.data.error.trim()
      ? parsed.data.error
      : fallback;
  }
}
