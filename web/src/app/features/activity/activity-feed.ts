import { Component, computed, inject, input, signal } from '@angular/core';
import { ActivityView, durationMs, sourceTypesFound, totalMs } from '../../core/activity';
import { I18n, TRANSLATIONS, TranslationKey } from '../../core/i18n';
import { IconComponent } from '../../shared/icon';
import { SourceBadgeComponent } from '../../shared/source-badge';

/**
 * What the agent is doing (or did) for a question: one row per real step reported by the server, with its status, how long
 * it took and, for the search and ranking steps, the kinds of source the information came from. Open while it runs or
 * after a failure; folded into one line once it ends well, and the user can open it again.
 */
@Component({
  selector: 'app-activity-feed',
  imports: [IconComponent, SourceBadgeComponent],
  template: `
    <section class="activity" [class.is-running]="running()" [class.is-errored]="errored()">
      <button type="button" class="activity-head" [attr.aria-expanded]="isOpen()" (click)="toggle()">
        <span class="activity-state" aria-hidden="true">
          @if (running()) { <span class="spinner"></span> } @else if (errored()) { <app-icon name="alert" /> } @else { <app-icon name="check" /> }
        </span>
        <span class="activity-title">{{ title() }}</span>
        @if (!isOpen()) {
          <span class="activity-found">
            @for (source of found(); track source.sourceType) {
              <app-source-badge [type]="source.sourceType" [count]="source.passages" />
            }
          </span>
        }
        <app-icon name="chevron-down" [class.chevron-open]="isOpen()" />
      </button>

      @if (isOpen()) {
        <ol class="activity-steps">
          @for (activity of visible(); track activity.id) {
            <li class="activity-step" [class]="'is-' + activity.status">
              <span class="step-state" aria-hidden="true">
                @switch (activity.status) {
                  @case ('running') { <span class="spinner"></span> }
                  @case ('completed') { <app-icon name="check" /> }
                  @case ('failed') { <app-icon name="alert" /> }
                  @default { <span class="ring"></span> }
                }
              </span>
              <div class="step-main">
                <span class="step-text">{{ text(activity) }}</span>
                @if (time(activity); as time) { <span class="step-time">{{ time }}</span> }
                @if (activity.detail?.sources?.length) {
                  <div class="step-sources">
                    @for (source of activity.detail?.sources; track source.sourceType) {
                      <app-source-badge [type]="source.sourceType" [count]="source.passages" />
                    }
                  </div>
                }
              </div>
            </li>
          }
        </ol>
      }
    </section>
  `,
})
export class ActivityFeedComponent {
  protected readonly i18n = inject(I18n);

  readonly activities = input.required<ActivityView[]>();
  readonly running = input(false);
  /** The question ended without an answer. */
  readonly errored = input(false);
  /** Seconds since the question was sent, shown in the title while it runs. */
  readonly elapsed = input(0);

  /** null = follow the state (open while running or after an error); a click fixes it. */
  private readonly chosen = signal<boolean | null>(null);
  protected readonly isOpen = computed(() => this.chosen() ?? (this.running() || this.errored()));

  /** Steps that have not started wait at the bottom, whatever the order they were announced in. */
  protected readonly visible = computed(() => {
    const all = this.activities();
    return [...all.filter((a) => a.status !== 'pending'), ...all.filter((a) => a.status === 'pending')];
  });
  protected readonly found = computed(() => sourceTypesFound(this.activities()));

  protected readonly title = computed(() => {
    if (this.running()) {
      return `${this.i18n.t('act.working')} ${this.elapsed()} s`;
    }

    if (this.errored()) {
      return this.i18n.t('act.failedHeader');
    }

    const total = totalMs(this.activities());
    const steps = this.i18n.t('act.steps', { n: this.activities().length });
    return total === null ? `${this.i18n.t('act.done')} · ${steps}` : `${this.i18n.t('act.done')} · ${steps} · ${seconds(total)}`;
  });

  protected toggle(): void {
    this.chosen.set(!this.isOpen());
  }

  protected time(activity: ActivityView): string | null {
    const ms = durationMs(activity);
    return ms === null ? null : seconds(ms);
  }

  /** The step in the user's language, with the numbers the server reported. Falls back to the server's English text. */
  protected text(activity: ActivityView): string {
    const detail = activity.detail;
    let key = `act.${activity.step}.${activity.status}`;
    if (activity.step === 'search_documents' && activity.status === 'completed' && (detail?.candidates ?? 0) === 0) {
      key = 'act.search_documents.none';
    } else if (activity.step === 'generate_answer' && activity.status === 'running' && (detail?.attempt ?? 1) > 1) {
      key = 'act.generate_answer.retry';
    }

    if (!(key in TRANSLATIONS.es)) {
      return activity.message;
    }

    const sources = detail?.sources ?? [];
    return this.i18n.t(key as TranslationKey, {
      passages: sources.reduce((sum, s) => sum + s.passages, 0),
      documents: sources.reduce((sum, s) => sum + s.documents, 0),
      candidates: detail?.candidates ?? 0,
      kept: detail?.kept ?? 0,
      cited: detail?.cited ?? 0,
    });
  }
}

function seconds(ms: number): string {
  return `${(ms / 1000).toFixed(1)} s`;
}
