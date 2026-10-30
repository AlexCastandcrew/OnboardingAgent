import { ActivityView, applyActivity, durationMs, failUnfinished, totalMs } from './activity';
import { ActivityStatus, ChatActivity } from './models';

const event = (id: string, status: ActivityStatus, second: number): ChatActivity => ({
  id, step: id.split('-')[0], status, message: id, timestamp: `2026-10-29T15:30:${String(second).padStart(2, '0')}Z`,
});

const fold = (events: ChatActivity[]): ActivityView[] => events.reduce<ActivityView[]>((list, e) => applyActivity(list, e), []);

describe('applyActivity', () => {
  it('applyActivity_RunningThenCompleted_KeepsOneRowWithItsDuration', () => {
    const list = fold([event('search', 'running', 1), event('search', 'completed', 4)]);

    expect(list.length).toBe(1);
    expect(list[0].status).toBe('completed');
    expect(durationMs(list[0])).toBe(3000);
  });

  it('applyActivity_RepeatedOrOutOfOrderEvent_IsIgnoredNotDuplicated', () => {
    const list = fold([event('search', 'running', 1), event('search', 'completed', 4), event('search', 'running', 2), event('search', 'completed', 5)]);

    expect(list.length).toBe(1);
    expect(list[0].timestamp).toBe('2026-10-29T15:30:04Z');
  });

  it('applyActivity_PendingStepThatStarts_MovesAfterTheStepsThatRanWhileItWaited', () => {
    const list = fold([
      event('search', 'completed', 2), event('generate-1', 'pending', 2), event('rank', 'running', 2), event('rank', 'completed', 4), event('generate-1', 'running', 4),
    ]);

    expect(list.map((a) => a.id)).toEqual(['search', 'rank', 'generate-1']);
    expect(list[2].startedAt).toBe('2026-10-29T15:30:04Z');
  });
});

describe('failUnfinished', () => {
  it('failUnfinished_RunningAndPendingSteps_BecomeFailedAndDoneOnesStay', () => {
    const list = failUnfinished(fold([event('understand', 'completed', 1), event('search', 'running', 1), event('generate-1', 'pending', 1)]));

    expect(list.map((a) => a.status)).toEqual(['completed', 'failed', 'failed']);
  });
});

describe('totalMs', () => {
  it('totalMs_StepsAcrossTheRun_IsFromTheFirstStartToTheLastEnd', () => {
    const list = fold([event('understand', 'running', 1), event('understand', 'completed', 3), event('search', 'running', 3), event('search', 'completed', 9)]);

    expect(totalMs(list)).toBe(8000);
  });

  it('totalMs_NothingFinishedYet_IsNull', () => {
    expect(totalMs(fold([event('understand', 'running', 1)]))).toBeNull();
  });
});
