import { ALL, filterPeople, rolesOf, summarize, teamsOf } from './admin';
import { PersonProgress } from './models';

const person = (over: Partial<PersonProgress>): PersonProgress => ({
  person: 'Person 01',
  team: 'silverwing',
  role: 'developer',
  level: 2,
  stepsDone: 2,
  stepsTotal: 4,
  day: 10,
  pace: 'onPace',
  lastActivityAt: '2026-10-29T10:00:00Z',
  sample: true,
  ...over,
});

const people = [
  person({ person: 'A', stepsDone: 4, day: 8, pace: 'ahead' }),
  person({ person: 'B', team: 'rhaegal', role: 'qa', stepsDone: 1, day: 12, pace: 'behind' }),
  person({ person: 'C', team: 'rhaegal', stepsDone: 2, day: 10 }),
];

describe('admin helpers', () => {
  it('filters by team and role, and "all" keeps everyone', () => {
    expect(filterPeople(people, { team: ALL, role: ALL }).length).toBe(3);
    expect(filterPeople(people, { team: 'rhaegal', role: ALL }).map((p) => p.person)).toEqual(['B', 'C']);
    expect(filterPeople(people, { team: 'rhaegal', role: 'qa' }).map((p) => p.person)).toEqual(['B']);
  });

  it('averages steps and day over the filtered people and counts who is ahead or behind', () => {
    const summary = summarize(people);

    expect(summary.people).toBe(3);
    expect(summary.averageSteps).toBeCloseTo(7 / 3);
    expect(summary.averageDay).toBeCloseTo(10);
    expect(summary.ahead).toBe(1);
    expect(summary.behind).toBe(1);
  });

  it('has no averages when nobody matches, instead of NaN', () => {
    const summary = summarize([]);

    expect(summary.people).toBe(0);
    expect(summary.averageSteps).toBeNull();
    expect(summary.averageDay).toBeNull();
  });

  it('lists the teams and roles present, sorted', () => {
    expect(teamsOf(people)).toEqual(['rhaegal', 'silverwing']);
    expect(rolesOf(people)).toEqual(['developer', 'qa']);
  });
});
