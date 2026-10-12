import { z } from 'zod';
import { TeamRole } from './TeamRole';
import { TeamRunStatus } from './TeamRunStatus';

export const teamRunStatusSchema = z.enum(TeamRunStatus);

export const teamRoleSchema = z.enum(TeamRole);

export const teamStepSchema = z.object({
  role: teamRoleSchema,
  status: teamRunStatusSchema,
  output: z.string().max(16000).nullable(),
  error: z.string().max(2000).nullable(),
});

export const teamRunSchema = z.object({
  id: z.uuid(),
  objective: z.string().min(1).max(2000),
  status: teamRunStatusSchema,
  createdAt: z.iso.datetime({ offset: true }),
  updatedAt: z.iso.datetime({ offset: true }),
  steps: z
    .array(teamStepSchema)
    .length(3)
    .refine(
      (steps) => new Set(steps.map((step) => step.role)).size === 3,
      'Each team role must appear once.',
    ),
  error: z.string().max(2000).nullable(),
});

export type TeamRun = z.infer<typeof teamRunSchema>;
