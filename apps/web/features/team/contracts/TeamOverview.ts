import { z } from 'zod';
import { teamRunSchema } from './TeamRun';

export const teamOverviewSchema = z.object({
  configured: z.boolean(),
  latestRun: teamRunSchema.nullable(),
});

export type TeamOverview = z.infer<typeof teamOverviewSchema>;
