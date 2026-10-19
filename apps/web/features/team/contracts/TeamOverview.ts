import { z } from 'zod';
import { teamRunSchema } from './TeamRun';
import { companyLoopOverviewSchema } from './CompanyLoopOverview';
import { newsPacketSchema } from './NewsPacket';

export const teamOverviewSchema = z.object({
  configured: z.boolean(),
  latestRun: teamRunSchema.nullable(),
  company: companyLoopOverviewSchema.nullable().optional(),
  news: newsPacketSchema.nullable().optional(),
});

export type TeamOverview = z.infer<typeof teamOverviewSchema>;
