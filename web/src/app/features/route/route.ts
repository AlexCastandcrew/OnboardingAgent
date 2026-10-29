import { Component, inject } from '@angular/core';
import { I18n } from '../../core/i18n';
import { SessionStore } from '../../core/session-store';

@Component({
  selector: 'app-route',
  template: `
    <section class="panel" [attr.aria-label]="i18n.t('route.heading')">
      <div class="panel-heading"><h2>{{ i18n.t('route.heading') }}</h2></div>
      <div class="panel-body">

      @if (store.loadingRoute()) {
        <p class="muted">{{ i18n.t('route.loading') }}</p>
      }

      @if (store.routeError(); as error) {
        <p class="error" role="alert">{{ error.title }}</p>
      }

      @for (step of store.route()?.steps ?? []; track step.step) {
        <article class="step" [class.pending]="step.status === 'pending'">
          <h3>
            {{ step.step }}. {{ step.title }}
            <span class="badge" [class.badge-pending]="step.status === 'pending'">
              {{ step.status === 'ready' ? i18n.t('route.ready') : i18n.t('route.pending') }}
            </span>
          </h3>
          <p>{{ step.goal }}</p>

          @if (step.status === 'pending') {
            <p class="muted">{{ i18n.t('route.pendingText') }}</p>
          }

          @for (doc of step.documents; track doc.documentId) {
            <p class="doc">📄 {{ doc.title }} <span class="muted">({{ doc.sourceType }})</span></p>
          }

          @for (link of step.links; track link.url) {
            <p><a [href]="link.url" target="_blank" rel="noopener noreferrer">{{ link.label }}</a></p>
          }

          @for (question of step.suggestedQuestions; track question) {
            <button type="button" class="suggestion" [disabled]="store.asking()" (click)="store.ask(question)">
              {{ question }}
            </button>
          }
        </article>
      }
      </div>
    </section>
  `,
})
export class RouteComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
}
