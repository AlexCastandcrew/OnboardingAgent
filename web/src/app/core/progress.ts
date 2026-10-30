import { OnboardingRoute, OnboardingStep } from './models';

/** Completed step keys per context (project|role|domain|initiative). The backend keeps no progress: this is the user's own marks. */
export type ProgressState = Record<string, string[]>;

export interface Progress {
  /** Steps backed by documents: the ones that can be done. Pending steps (no documents) are not counted. */
  total: number;
  done: number;
  percent: number;
  /** First ready step not done yet, or null when all are done or none is ready. */
  current: OnboardingStep | null;
  pendingSteps: number;
}

export interface Recommendation {
  step: OnboardingStep;
  /** "next" for the step to do now, "later" for the ones after it. */
  reason: 'next' | 'later';
  /** The question the action sends to the agent. */
  question: string;
}

export function contextKey(projectId: string, role: string, domain: string | null, initiative: string | null): string {
  return [projectId, role, domain ?? '', initiative ?? ''].join('|');
}

export function computeProgress(route: OnboardingRoute | null, doneKeys: readonly string[]): Progress {
  const ready = route?.steps.filter((s) => s.status === 'ready') ?? [];
  const done = ready.filter((s) => doneKeys.includes(s.key)).length;
  return {
    total: ready.length,
    done,
    percent: ready.length === 0 ? 0 : Math.round((done / ready.length) * 100),
    current: ready.find((s) => !doneKeys.includes(s.key)) ?? null,
    pendingSteps: (route?.steps.length ?? 0) - ready.length,
  };
}

/** Rule-based, not AI: the next undone ready steps in route order, each with the first question the route suggests for it. */
export function recommend(route: OnboardingRoute | null, doneKeys: readonly string[], max = 3): Recommendation[] {
  const open = route?.steps.filter((s) => s.status === 'ready' && !doneKeys.includes(s.key)) ?? [];
  return open
    .slice(0, max)
    .map((step, index): Recommendation => ({ step, reason: index === 0 ? 'next' : 'later', question: step.suggestedQuestions[0] ?? step.title }));
}

/** The step a suggested question belongs to, so asking it can count as doing that step. */
export function stepOfQuestion(route: OnboardingRoute | null, question: string): OnboardingStep | null {
  return route?.steps.find((s) => s.status === 'ready' && s.suggestedQuestions.includes(question)) ?? null;
}

export function toggleDone(state: ProgressState, key: string, step: string): ProgressState {
  const current = state[key] ?? [];
  return { ...state, [key]: current.includes(step) ? current.filter((s) => s !== step) : [...current, step] };
}
