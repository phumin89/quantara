import { z } from 'zod';
import { teamRunSchema } from './TeamRun';

export const dailyBriefSchema = z.object({
  date: z.iso.date(),
  runs: z.array(teamRunSchema),
});

export type DailyBrief = z.infer<typeof dailyBriefSchema>;
