import { z } from 'zod';
import { CompanyLoopStatus } from './CompanyLoopStatus';

export const companyLoopOverviewSchema = z.object({
  enabled: z.boolean(),
  status: z.enum(CompanyLoopStatus),
  nextRunAt: z.iso.datetime({ offset: true }).nullable(),
  lastRunId: z.uuid().nullable(),
  error: z.string().max(2000).nullable(),
});

export type CompanyLoopOverview = z.infer<typeof companyLoopOverviewSchema>;
