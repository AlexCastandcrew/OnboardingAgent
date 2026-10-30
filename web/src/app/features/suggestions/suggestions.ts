import { Component, inject } from '@angular/core';
import { I18n, TranslationKey } from '../../core/i18n';
import { SessionStore } from '../../core/session-store';
import { IconComponent, IconName } from '../../shared/icon';

interface Category {
  id: string;
  icon: IconName;
  title: TranslationKey;
  description: TranslationKey;
  questions: TranslationKey[];
}

const CATEGORIES: readonly Category[] = [
  { id: 'arch', icon: 'layers', title: 'sug.arch.title', description: 'sug.arch.desc', questions: ['sug.arch.q1', 'sug.arch.q2'] },
  { id: 'code', icon: 'code', title: 'sug.code.title', description: 'sug.code.desc', questions: ['sug.code.q1', 'sug.code.q2'] },
  { id: 'team', icon: 'users', title: 'sug.team.title', description: 'sug.team.desc', questions: ['sug.team.q1', 'sug.team.q2'] },
  { id: 'onb', icon: 'compass', title: 'sug.onb.title', description: 'sug.onb.desc', questions: ['sug.onb.q1', 'sug.onb.q2'] },
];

/** Starter questions by theme, shown while there is no conversation. A click sends the question like any other. */
@Component({
  selector: 'app-suggestions',
  imports: [IconComponent],
  template: `
    <section class="suggestions" [attr.aria-label]="i18n.t('sug.heading')">
      @for (category of categories; track category.id) {
        <article class="suggestion-card">
          <header>
            <span class="suggestion-icon"><app-icon [name]="category.icon" /></span>
            <div>
              <h3>{{ i18n.t(category.title) }}</h3>
              <p>{{ i18n.t(category.description) }}</p>
            </div>
          </header>
          @for (question of category.questions; track question) {
            <button type="button" class="suggestion-question" [disabled]="store.asking()" (click)="store.ask(i18n.t(question))">
              {{ i18n.t(question) }}
            </button>
          }
        </article>
      }
    </section>
  `,
})
export class SuggestionsComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly categories = CATEGORIES;
}
