import { Component, computed, inject, signal } from '@angular/core';
import { I18n } from '../../core/i18n';
import { ROLES } from '../../core/models';
import { SessionStore } from '../../core/session-store';
import { IconComponent } from '../../shared/icon';
import { SourceBadgeComponent } from '../../shared/source-badge';

/** Where the user stands in the route. Everything shown is derived from the route and the steps the user marked or asked about. */
@Component({
  selector: 'app-onboarding-progress',
  imports: [IconComponent],
  template: `
    <section class="home-card progress-card" [attr.aria-label]="i18n.t('home.progress.title')">
      <header>
        <h2>{{ i18n.t('home.progress.title') }}</h2>
        @if (progress().total > 0) {
          <span class="progress-percent">{{ i18n.t('home.progress.percent', { n: progress().percent }) }}</span>
        }
      </header>

      @if (store.loadingRoute() && store.route() === null) {
        <p class="muted">{{ i18n.t('route.loading') }}</p>
      } @else if (progress().total === 0) {
        <p class="muted">{{ i18n.t('home.progress.empty') }}</p>
      } @else {
        <div
          class="progress-bar"
          role="progressbar"
          aria-valuemin="0"
          aria-valuemax="100"
          [attr.aria-valuenow]="progress().percent"
          [attr.aria-label]="i18n.t('home.progress.title')">
          <span [style.width.%]="progress().percent"></span>
        </div>
        <dl class="progress-facts">
          <div><dt>{{ i18n.t('home.progress.milestones') }}</dt><dd>{{ progress().done }} / {{ progress().total }}</dd></div>
          <div><dt>{{ i18n.t('home.progress.role') }}</dt><dd>{{ roleLabel() }}</dd></div>
          <div>
            <dt>{{ i18n.t('home.progress.stage') }}</dt>
            <dd>{{ progress().current ? progress().current!.title : i18n.t('home.progress.allDone') }}</dd>
          </div>
        </dl>
        @if (progress().current; as current) {
          <button type="button" class="btn-primary" [disabled]="store.asking()" (click)="store.ask(firstQuestion())">
            {{ i18n.t('home.progress.continue') }}<app-icon name="arrow-up" />
          </button>
        }
        <p class="muted progress-note">
          {{ i18n.t('home.progress.note') }}
          @if (progress().pendingSteps > 0) {
            {{ i18n.t('home.progress.pending', { n: progress().pendingSteps }) }}
          }
        </p>
      }
    </section>
  `,
})
export class OnboardingProgressComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly progress = this.store.progress;
  protected readonly roleLabel = computed(() => ROLES.find((r) => r.id === this.store.role())?.label ?? this.store.role());
  protected readonly firstQuestion = computed(() => {
    const step = this.progress().current;
    return step ? (step.suggestedQuestions[0] ?? step.title) : '';
  });
}

/** Next steps picked by rules from the route and the progress, not by the AI; the button asks the agent about the step. */
@Component({
  selector: 'app-recommendations',
  imports: [IconComponent],
  template: `
    @if (store.recommendations().length > 0) {
      <section class="home-section" [attr.aria-label]="i18n.t('home.next.title')">
        <h2>{{ i18n.t('home.next.title') }}</h2>
        <div class="next-grid">
          @for (item of store.recommendations(); track item.step.key) {
            <article class="home-card next-card">
              <h3>{{ item.step.title }}</h3>
              <p>{{ item.step.goal }}</p>
              <p class="next-reason muted">
                {{ i18n.t(item.reason === 'next' ? 'home.next.reasonNext' : 'home.next.reasonLater') }}
              </p>
              <button type="button" [disabled]="store.asking()" (click)="store.ask(item.question)">
                <app-icon name="sparkles" />{{ i18n.t('home.next.ask') }}
              </button>
            </article>
          }
        </div>
        <p class="muted">{{ i18n.t('home.next.note') }}</p>
      </section>
    }
  `,
})
export class RecommendationsComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
}

/** What the index really holds for the project. Integrations are inferred from the indexed source types, never assumed. */
@Component({
  selector: 'app-project-glance',
  imports: [IconComponent, SourceBadgeComponent],
  template: `
    <section class="home-card glance-card" [attr.aria-label]="i18n.t('home.glance.title')">
      <header>
        <h2>{{ i18n.t('home.glance.title') }}</h2>
        @if (store.knowledge(); as k) {
          <button type="button" class="glance-toggle" [attr.aria-expanded]="expanded()" (click)="expanded.set(!expanded())">
            {{ i18n.t(expanded() ? 'home.glance.less' : 'home.glance.more') }}
            <app-icon name="chevron-down" />
          </button>
        }
      </header>

      @if (store.knowledge(); as k) {
        <dl class="glance-metrics">
          <div><dt>{{ i18n.t('home.glance.documents') }}</dt><dd>{{ k.documents }}</dd></div>
          <div><dt>{{ i18n.t('home.glance.passages') }}</dt><dd>{{ k.passages }}</dd></div>
          <div><dt>{{ i18n.t('home.glance.repositories') }}</dt><dd>{{ k.repositories.length }}</dd></div>
          <div><dt>{{ i18n.t('home.glance.teams') }}</dt><dd>{{ k.teams }}</dd></div>
        </dl>

        @if (expanded()) {
          <div class="glance-detail">
            <h3>{{ i18n.t('home.glance.connections') }}</h3>
            <ul class="glance-connections">
              @for (c of connections(); track c.id) {
                <li>
                  @if (c.type) {
                    <app-source-badge [type]="c.type" />
                  } @else {
                    <span class="src-badge src-other">{{ c.label }}</span>
                  }
                  <span [class.connected]="c.documents > 0" class="connection-state">
                    {{ c.documents > 0 ? i18n.t('home.glance.indexed', { n: c.documents }) : i18n.t(c.missing) }}
                  </span>
                </li>
              }
            </ul>

            @if (k.repositories.length > 0) {
              <h3>{{ i18n.t('home.glance.repositories') }}</h3>
              <ul class="glance-repos">
                @for (repo of k.repositories; track repo.url) {
                  <li>
                    <a [href]="repo.url" target="_blank" rel="noopener noreferrer">{{ repo.name }}</a>
                    <span class="muted">{{ i18n.t('home.glance.repoDocs', { n: repo.documents }) }}@if (repo.version) { · {{ repo.version }} }</span>
                  </li>
                }
              </ul>
            }

            <p class="muted">
              {{ i18n.t('home.glance.initiatives', { n: k.initiatives }) }}
              @if (k.lastIndexedAt) { · {{ i18n.t('home.glance.updated', { date: updated() }) }} }
            </p>
            <p class="muted">{{ i18n.t('home.glance.note') }}</p>
          </div>
        }
      } @else if (store.knowledgeFailed()) {
        <p class="muted">{{ i18n.t('home.glance.unavailable') }}</p>
      } @else {
        <p class="muted">{{ i18n.t('home.glance.loading') }}</p>
      }
    </section>
  `,
})
export class ProjectGlanceComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly expanded = signal(false);

  protected readonly connections = computed(() => {
    const counts = new Map((this.store.knowledge()?.sourceTypes ?? []).map((s) => [s.sourceType, s.documents]));
    return [
      { id: 'confluence', type: 'confluence', label: 'Confluence', documents: counts.get('confluence') ?? 0, missing: 'home.glance.notIndexed' as const },
      { id: 'jira', type: 'jira', label: 'Jira', documents: counts.get('jira') ?? 0, missing: 'home.glance.notIndexed' as const },
      { id: 'git', type: 'repo', label: 'Git', documents: counts.get('repo') ?? 0, missing: 'home.glance.notIndexed' as const },
      // There is no Azure DevOps integration: it is listed so the gap is explicit, not hidden.
      { id: 'ado', type: null, label: 'Azure DevOps', documents: 0, missing: 'home.glance.notConnected' as const },
    ];
  });

  protected readonly updated = computed(() => {
    const at = this.store.knowledge()?.lastIndexedAt;
    return at ? new Date(at).toLocaleDateString(this.i18n.language()) : '';
  });
}
