import { z } from 'zod';

const timestampSchema = z.iso.datetime({ offset: true });

const newsEventSchema = z.strictObject({
  eventId: z.string().min(1),
  revision: z.number().int().positive(),
  sourceId: z.string().min(1),
  title: z.string().min(1).max(500),
  url: z.url().refine((value) => value.startsWith('https://')),
  publishedAt: timestampSchema.nullable(),
  firstSeenAt: timestampSchema,
  revisionSeenAt: timestampSchema,
  fetchedAt: timestampSchema,
  expiresAt: timestampSchema.nullable(),
  symbols: z.array(z.string().min(1).max(30)).max(32),
  marketWide: z.boolean(),
  category: z.string().min(1).max(80),
  factStatus: z.string().min(1).max(80),
  contentHash: z.string().min(1).max(128),
});

const sourceStateSchema = z.strictObject({
  id: z.string().min(1),
  label: z.string().min(1),
  url: z.url().refine((value) => value.startsWith('https://')),
  status: z.string().min(1),
  lastSuccessAt: timestampSchema.nullable(),
  nextRetryAt: timestampSchema.nullable(),
  reason: z.string().nullable(),
});

export const newsPacketSchema = z
  .strictObject({
    schema: z.literal('news-v1'),
    observedAt: timestampSchema,
    executionAuthority: z.literal(false),
    events: z.array(newsEventSchema).max(5),
    sourceStates: z.array(sourceStateSchema).max(32),
    omittedEventCount: z.number().int().nonnegative(),
    pilot: z.strictObject({
      startedAt: timestampSchema.nullable(),
      lastPollAt: timestampSchema.nullable(),
      completed72Hours: z.boolean(),
      gapCount: z.number().int().nonnegative(),
    }),
  })
  .refine(
    (packet) => {
      if (!packet.pilot.completed72Hours) {
        return true;
      }

      const sources = packet.sourceStates.filter(
        (source) => source.id !== 'bitkub',
      );
      const ticksPerSecond = BigInt(10000000);
      const [observed, started, polled, ...successes] = [
        packet.observedAt,
        packet.pilot.startedAt,
        packet.pilot.lastPollAt,
        ...sources.map((source) => source.lastSuccessAt),
      ].map((value) => {
        if (!value) {
          return null;
        }

        const milliseconds = Date.parse(value);
        if (!Number.isFinite(milliseconds)) {
          return null;
        }

        // Keep the backend's 100ns precision at completion/freshness boundaries.
        const fraction = /\.(\d+)/.exec(value)?.[1]?.slice(3, 7) ?? '';

        return (
          BigInt(milliseconds) * BigInt(10000) + BigInt(fraction.padEnd(4, '0'))
        );
      });
      if (observed == null || started == null || polled == null) {
        return false;
      }

      return (
        observed - started >= BigInt(72 * 60 * 60) * ticksPerSecond &&
        started <= polled &&
        polled <= observed + BigInt(5) * ticksPerSecond &&
        observed - polled <= BigInt(10 * 60) * ticksPerSecond &&
        new Set(packet.sourceStates.map((source) => source.id)).size ===
          packet.sourceStates.length &&
        sources.length === 2 &&
        sources.every((source, index) => {
          const success = successes[index];

          return (
            ['federal-reserve', 'bls'].includes(source.id) &&
            ['Healthy', 'NoNewEvents'].includes(source.status) &&
            success != null &&
            success <= observed + BigInt(5) * ticksPerSecond &&
            observed - success <= BigInt(15 * 60) * ticksPerSecond
          );
        })
      );
    },
    { message: 'Completed news pilot is missing current source evidence.' },
  );

export type NewsPacket = z.infer<typeof newsPacketSchema>;
