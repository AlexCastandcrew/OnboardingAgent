import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { I18n } from '../../core/i18n';
import { SessionStore } from '../../core/session-store';

@Component({
  selector: 'app-evidence',
  imports: [DatePipe],
  template: `
    @if (store.evidence(); as evidence) {
      <aside class="evidence" [attr.aria-label]="i18n.t('evidence.label')">
        <header>
          <h2>{{ evidence.source.title }}</h2>
          <button type="button" (click)="store.closeEvidence()" [attr.aria-label]="i18n.t('evidence.close')">✕</button>
        </header>

        <div class="evidence-body">
        <p class="muted">
          {{ evidence.source.sourceType }} · {{ i18n.t('evidence.section') }} «{{ evidence.source.section }}»
          @if (evidence.source.sourceVersion) {
            · {{ i18n.t('evidence.version') }} {{ evidence.source.sourceVersion }}
          }
        </p>

        <h3>{{ i18n.t('evidence.excerpt') }}</h3>
        <blockquote>{{ evidence.source.excerpt }}</blockquote>

        @if (evidence.source.url) {
          <p><a [href]="evidence.source.url" target="_blank" rel="noopener noreferrer">{{ i18n.t('evidence.openSource') }}</a></p>
        }

        @if (evidence.loading) {
          <p class="muted">{{ i18n.t('evidence.loading') }}</p>
        }

        @if (evidence.error; as error) {
          <p class="error" role="alert">{{ error.title }}</p>
        }

        @if (evidence.document; as doc) {
          <h3>{{ i18n.t('evidence.fullDocument') }}</h3>
          <dl>
            <dt>{{ i18n.t('evidence.path') }}</dt><dd>{{ doc.path }}</dd>
            <dt>{{ i18n.t('evidence.versionLabel') }}</dt><dd>{{ doc.sourceVersion ?? '—' }}</dd>
            <dt>{{ i18n.t('evidence.sourceDate') }}</dt><dd>{{ doc.sourceUpdatedAt ? (doc.sourceUpdatedAt | date: 'yyyy-MM-dd') : i18n.t('evidence.notRecorded') }}</dd>
            <dt>{{ i18n.t('evidence.ingestedDate') }}</dt><dd>{{ doc.ingestedAt | date: 'yyyy-MM-dd HH:mm' }}</dd>
          </dl>
          <p class="muted">{{ i18n.t('evidence.snapshotNote') }}</p>
          <pre>{{ doc.content }}</pre>
        }
        </div>
      </aside>
    }
  `,
})
export class EvidenceComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
}
