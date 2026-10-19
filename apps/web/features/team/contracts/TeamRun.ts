import { z } from 'zod';
import { TeamRole } from './TeamRole';
import { TeamRunStatus } from './TeamRunStatus';
import { learningLessonSchema } from './LearningLesson';
import { tradeAssessmentSchema } from './TradeAssessment';

export const teamRunStatusSchema = z.enum(TeamRunStatus);

export const teamRoleSchema = z.enum(TeamRole);

export const teamStepUsageSchema = z
  .object({
    inputTokens: z.number().int().nonnegative(),
    outputTokens: z.number().int().nonnegative(),
    cachedInputTokens: z.number().int().nonnegative(),
  })
  .refine(
    (usage) => usage.cachedInputTokens <= usage.inputTokens,
    'Cached usage must be part of input usage.',
  );

export const teamStepSchema = z.object({
  role: teamRoleSchema,
  status: teamRunStatusSchema,
  output: z.string().max(16000).nullable(),
  error: z.string().max(2000).nullable(),
  accountKey: z.string().nullable().optional(),
  startedAt: z.iso.datetime({ offset: true }).nullable().optional(),
  completedAt: z.iso.datetime({ offset: true }).nullable().optional(),
  inputDigest: z
    .string()
    .regex(/^[a-f0-9]{64}$/)
    .nullable()
    .optional(),
  usage: teamStepUsageSchema.nullable().optional(),
});

export const teamRunSchema = z
  .object({
    id: z.uuid(),
    objective: z.string().min(1).max(2000),
    status: teamRunStatusSchema,
    createdAt: z.iso.datetime({ offset: true }),
    updatedAt: z.iso.datetime({ offset: true }),
    steps: z.array(teamStepSchema).refine((steps) => {
      const [first, second, third] = steps;

      return (
        (first !== undefined &&
          second !== undefined &&
          steps.length === 2 &&
          (first.role === TeamRole.Ceo ||
            [
              TeamRole.Intraday,
              TeamRole.Swing,
              TeamRole.News,
              TeamRole.Positions,
              TeamRole.Quant,
            ].includes(first.role)) &&
          second.role === TeamRole.Reviewer) ||
        (first !== undefined &&
          second !== undefined &&
          third !== undefined &&
          steps.length === 3 &&
          first.role === TeamRole.Ceo &&
          second.role === TeamRole.Analyst &&
          third.role === TeamRole.Reviewer)
      );
    }, 'Team steps must be a supported specialist-to-reviewer or legacy handoff.'),
    error: z.string().max(2000).nullable(),
    accountKey: z.string().min(1).max(128).nullable().optional(),
    lesson: learningLessonSchema.nullable().optional(),
    assessment: tradeAssessmentSchema.nullable().optional(),
  })
  .refine(
    (run) =>
      !run.assessment ||
      (run.assessment.runId === run.id &&
        run.assessment.accountKey === run.accountKey),
    'Trade assessment must belong to this run and account.',
  );

export type TeamRun = z.infer<typeof teamRunSchema>;
