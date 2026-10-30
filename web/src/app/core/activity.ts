import { ActivityStatus, ChatActivity, SourceTypeCount } from './models';

/** An activity plus the time it started running, so its duration can be shown once it ends. */
export interface ActivityView extends ChatActivity {
  startedAt: string | null;
}

const RANK: Record<ActivityStatus, number> = { pending: 0, running: 1, completed: 2, failed: 2 };

/**
 * Folds one server event into the list: one row per id, in the order the steps really started. A row never goes
 * backwards (a late or repeated event is ignored), and a pending step that starts moves to the end, after the steps
 * that ran while it waited.
 */
export function applyActivity(list: readonly ActivityView[], event: ChatActivity): ActivityView[] {
  const index = list.findIndex((a) => a.id === event.id);
  if (index === -1) {
    return [...list, { ...event, startedAt: event.status === 'running' ? event.timestamp : null }];
  }

  const current = list[index];
  if (RANK[event.status] <= RANK[current.status]) {
    return [...list];
  }

  const updated: ActivityView = { ...event, startedAt: current.startedAt ?? (event.status === 'running' ? event.timestamp : null) };
  return current.status === 'pending'
    ? [...list.slice(0, index), ...list.slice(index + 1), updated]
    : list.map((a, i) => (i === index ? updated : a));
}

/** The stream broke: whatever was waiting or running did not finish. */
export function failUnfinished(list: readonly ActivityView[]): ActivityView[] {
  return list.map((a) => (a.status === 'running' || a.status === 'pending' ? { ...a, status: 'failed' } : a));
}

/** Milliseconds a finished step took, or null while it runs or when it never started. */
export function durationMs(activity: ActivityView): number | null {
  if (activity.startedAt === null || (activity.status !== 'completed' && activity.status !== 'failed')) {
    return null;
  }

  const ms = Date.parse(activity.timestamp) - Date.parse(activity.startedAt);
  return Number.isFinite(ms) && ms >= 0 ? ms : null;
}

/** Time from the first step starting to the last one ending, or null when nothing has finished. */
export function totalMs(list: readonly ActivityView[]): number | null {
  const starts = list.map((a) => a.startedAt).filter((s): s is string => s !== null).map(Date.parse);
  const ends = list.filter((a) => a.status === 'completed' || a.status === 'failed').map((a) => Date.parse(a.timestamp));
  if (starts.length === 0 || ends.length === 0) {
    return null;
  }

  const ms = Math.max(...ends) - Math.min(...starts);
  return Number.isFinite(ms) && ms >= 0 ? ms : null;
}

/** The source types the search found, merged and ordered by passages: where the information came from. */
export function sourceTypesFound(list: readonly ActivityView[]): SourceTypeCount[] {
  const search = list.find((a) => a.step === 'search_documents' && a.status === 'completed');
  return [...(search?.detail?.sources ?? [])];
}
