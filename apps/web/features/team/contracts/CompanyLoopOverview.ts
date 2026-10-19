import { z } from 'zod';
import { CompanyLoopStatus } from './CompanyLoopStatus';
import { TeamRole } from './TeamRole';

export const companyLoopOverviewSchema = z.object({
  enabled: z.boolean(),
  status: z.enum(CompanyLoopStatus),
  nextRunAt: z.iso.datetime({ offset: true }).nullable(),
  lastRunId: z.uuid().nullable(),
  error: z.string().max(2000).nullable(),
  lastCompletedAt: z.iso.datetime({ offset: true }).nullable().optional(),
  activeRole: z.enum(TeamRole).nullable().optional(),
  nextRole: z.enum(TeamRole).nullable().optional(),
  budgetReason: z.string().max(500).nullable().optional(),
  budgetMode: z.string().max(100).nullable().optional(),
});

export type CompanyLoopOverview = z.infer<typeof companyLoopOverviewSchema>;
