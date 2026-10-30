import { Component, computed, inject, signal } from '@angular/core';
import { I18n } from '../../core/i18n';
import { SessionStore } from '../../core/session-store';
import { IconComponent } from '../../shared/icon';
import { SourceBadgeComponent } from '../../shared/source-badge';

/**
 * The onboarding route as a guided path: numbered steps joined by a line. Each step shows its title, status and goal;
 * its documents and suggested questions fold away so a long route stays short. Only the first step starts open.
 */
@Component({
  selector: 'app-route',
  imports: [IconComponent, SourceBadgeComponent],
  template: `
    <section class="side-section" [attr.aria-label]="i18n.t('route.heading')">
      <div class="side-title-row">
        <h2 class="side-title">{{ i18n.t('route.heading') }}</h2>
        @if (steps().length > 0) {
          <button type="button" class="link-button" (click)="toggleAll()">
            {{ allOpen() ? i18n.t('route.collapseAll') : i18n.t('route.expandAll') }}
          </button>
        }
      </div>

      @if (store.loadingRoute()) {
        <p class="side-muted">{{ i18n.t('route.loading') }}</p>
      }

      @if (store.routeError(); as error) {
        <p class="side-error" role="alert">{{ error.title }}</p>
      }

      <ol class="steps">
        @for (step of steps(); track step.step) {
          @let open = isOpen(step.step);
          @let done = store.doneSteps().includes(step.key);
          <li class="step" [class.pending]="step.status === 'pending'" [class.done]="done">
            <span class="step-number" aria-hidden="true">
              @if (done) { <app-icon name="check" /> } @else { {{ step.step }} }
            </span>

            <div class="step-body">
              <div class="step-head">
                <h3>
                  <button
                    type="button"
                    class="step-toggle"
                    [attr.aria-expanded]="open"
                    [attr.aria-controls]="'step-details-' + step.step"
                    (click)="toggle(step.step)">
                    <span>{{ step.step }}. {{ step.title }}</span>
                    <app-icon name="chevron-down" [class.chevron-open]="open" />
                  </button>
                </h3>
                <span class="status-pill" [class.is-pending]="step.status === 'pending'">
                  {{ step.status === 'ready' ? i18n.t('route.ready') : i18n.t('route.pending') }}
                </span>
              </div>
              <p class="step-goal">{{ step.goal }}</p>

              @if (step.status === 'pending') {
                <p class="side-muted">{{ i18n.t('route.pendingText') }}</p>
              }

              @if (open) {
                <div class="step-details" [id]="'step-details-' + step.step">
                  @if (step.status === 'ready') {
                    <button type="button" class="step-question" (click)="store.toggleStepDone(step.key)">
                      <app-icon name="check" />{{ i18n.t(done ? 'route.markUndone' : 'route.markDone') }}
                    </button>
                  }
                  @if (step.documents.length > 0 || step.links.length > 0) {
                    <p class="step-label">{{ i18n.t('route.documents') }}</p>
                    <ul class="step-docs">
                      @for (doc of step.documents; track doc.documentId) {
                        <li><app-source-badge [type]="doc.sourceType" /><span>{{ doc.title }}</span></li>
                      }
                      @for (link of step.links; track link.url) {
                        <li><app-icon name="link" /><a [href]="link.url" target="_blank" rel="noopener noreferrer">{{ link.label }}</a></li>
                      }
                    </ul>
                  }

                  @if (step.suggestedQuestions.length > 0) {
                    <p class="step-label">{{ i18n.t('route.questions') }}</p>
                    @for (question of step.suggestedQuestions; track question) {
                      <button type="button" class="step-question" [disabled]="store.asking()" (click)="store.ask(question)">
                        {{ question }}
                      </button>
                    }
                  }
                </div>
              }
            </div>
          </li>
        }
      </ol>
    </section>
  `,
})
export class RouteComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);

  protected readonly steps = computed(() => this.store.route()?.steps ?? []);
  private readonly expanded = signal<ReadonlySet<number>>(new Set([1]));
  protected readonly allOpen = computed(() => this.steps().length > 0 && this.steps().every((s) => this.expanded().has(s.step)));

  protected isOpen(step: number): boolean {
    return this.expanded().has(step);
  }

  protected toggle(step: number): void {
    const next = new Set(this.expanded());
    if (!next.delete(step)) {
      next.add(step);
    }

    this.expanded.set(next);
  }

  protected toggleAll(): void {
    this.expanded.set(this.allOpen() ? new Set() : new Set(this.steps().map((s) => s.step)));
  }
}
