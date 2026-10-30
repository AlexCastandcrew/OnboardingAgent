import { PersonProgress } from './models';

export const ALL = 'all';

export interface AdminFilters {
  team: string;
  role: string;
}

export interface AdminSummary {
  people: number;
  /** Average steps done, or null with nobody in the filter. */
  averageSteps: number | null;
  averageTotal: number | null;
  /** Average day of the onboarding, or null with nobody in the filter. */
  averageDay: number | null;
  behind: number;
  ahead: number;
}

export function filterPeople(people: readonly PersonProgress[], filters: AdminFilters): PersonProgress[] {
  return people.filter((p) => (filters.team === ALL || p.team === filters.team) && (filters.role === ALL || p.role === filters.role));
}

export function teamsOf(people: readonly PersonProgress[]): string[] {
  return [...new Set(people.map((p) => p.team))].sort();
}

export function rolesOf(people: readonly PersonProgress[]): string[] {
  return [...new Set(people.map((p) => p.role))].sort();
}

function average(values: readonly number[]): number | null {
  return values.length === 0 ? null : values.reduce((sum, v) => sum + v, 0) / values.length;
}

export function summarize(people: readonly PersonProgress[]): AdminSummary {
  return {
    people: people.length,
    averageSteps: average(people.map((p) => p.stepsDone)),
    averageTotal: average(people.map((p) => p.stepsTotal)),
    averageDay: average(people.map((p) => p.day)),
    behind: people.filter((p) => p.pace === 'behind').length,
    ahead: people.filter((p) => p.pace === 'ahead').length,
  };
}
