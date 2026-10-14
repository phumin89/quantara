import { z } from 'zod';

export const learningLessonSchema = z.object({
  lesson: z.string().max(500).trim().min(1),
  evidence: z.string().max(500).trim().min(1),
  nextTest: z.string().max(500).trim().min(1),
});

export type LearningLesson = z.infer<typeof learningLessonSchema>;
