import { OnboardingRoute, OnboardingStep } from './models';
import { computeProgress, recommend, stepOfQuestion, toggleDone } from './progress';

function step(key: string, status: 'ready' | 'pending', questions: string[] = []): OnboardingStep {
  return { step: 1, key, title: key, goal: '', status, documentIds: [], documents: [], links: [], suggestedQuestions: questions };
}

const route: OnboardingRoute = {
  projectId: 'htg',
  role: 'developer',
  domain: null,
  initiative: null,
  steps: [step('a', 'ready', ['qa']), step('b', 'pending', ['qb']), step('c', 'ready', ['qc']), step('d', 'ready'), step('e', 'ready')],
};

describe('computeProgress', () => {
  it('computeProgress_PendingStepsPresent_CountsOnlyReadyStepsInThePercentage', () => {
    const progress = computeProgress(route, ['a', 'b']);

    expect(progress.total).toBe(4);
    expect(progress.done).toBe(1);
    expect(progress.percent).toBe(25);
    expect(progress.pendingSteps).toBe(1);
    expect(progress.current?.key).toBe('c');
  });

  it('computeProgress_NoRoute_ReturnsZeroWithoutCurrentStep', () => {
    expect(computeProgress(null, [])).toEqual({ total: 0, done: 0, percent: 0, current: null, pendingSteps: 0 });
  });
});

describe('recommend', () => {
  it('recommend_SomeDone_SkipsDoneAndPendingStepsAndFallsBackToTheTitleWithoutQuestions', () => {
    const items = recommend(route, ['a']);

    expect(items.map((i) => i.step.key)).toEqual(['c', 'd', 'e']);
    expect(items.map((i) => i.reason)).toEqual(['next', 'later', 'later']);
    expect(items.map((i) => i.question)).toEqual(['qc', 'd', 'e']);
  });
});

describe('stepOfQuestion', () => {
  it('stepOfQuestion_QuestionOfPendingStep_ReturnsNull', () => {
    expect(stepOfQuestion(route, 'qb')).toBeNull();
    expect(stepOfQuestion(route, 'qc')?.key).toBe('c');
  });
});

describe('toggleDone', () => {
  it('toggleDone_TwiceOnTheSameStep_AddsThenRemovesWithoutTouchingOtherContexts', () => {
    const marked = toggleDone({ other: ['x'] }, 'k', 'a');
    const unmarked = toggleDone(marked, 'k', 'a');

    expect(marked).toEqual({ other: ['x'], k: ['a'] });
    expect(unmarked).toEqual({ other: ['x'], k: [] });
  });
});
