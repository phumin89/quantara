import { z } from 'zod';
import { teamRunSchema } from './TeamRun';
import { companyLoopOverviewSchema } from './CompanyLoopOverview';

export const teamOverviewSchema = z.object({
  configured: z.boolean(),
  latestRun: teamRunSchema.nullable(),
  company: companyLoopOverviewSchema.nullable().optional(),
});

export type TeamOverview = z.infer<typeof teamOverviewSchema>;
