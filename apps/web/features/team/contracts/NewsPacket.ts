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

export const newsPacketSchema = z.strictObject({
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
});

export type NewsPacket = z.infer<typeof newsPacketSchema>;
