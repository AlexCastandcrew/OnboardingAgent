import { Component, computed, inject, input } from '@angular/core';
import { I18n, TranslationKey } from '../../core/i18n';
import { Inline, parseAnswer } from '../../core/markdown';
import { ChatResponse, EvidenceStatus } from '../../core/models';
import { SessionStore } from '../../core/session-store';

const STATUS_KEY: Record<EvidenceStatus, TranslationKey> = {
  supported: 'answer.supported',
  partial: 'answer.partial',
  insufficient: 'answer.insufficient',
};

/** Bold, code and citation chips of one line of an answer. Text nodes only, never HTML. */
@Component({
  selector: 'app-inline',
  template: `@for (item of items(); track $index) {@if (item.kind === 'citation') {<button type="button" class="chip" (click)="store.openEvidence(item.source)">{{ item.label }}</button>} @else if (item.bold) {<strong>{{ item.text }}</strong>} @else if (item.code) {<code>{{ item.text }}</code>} @else {<span>{{ item.text }}</span>}}`,
})
export class InlineComponent {
  protected readonly store = inject(SessionStore);

  readonly items = input.required<Inline[]>();
}

@Component({
  selector: 'app-answer',
  imports: [InlineComponent],
  template: `
    <article class="answer" [class]="'status-' + response().evidenceStatus">
      <p class="status">{{ label() }}</p>

      <div class="text">
        @for (block of blocks(); track $index) {
          @if (block.kind === 'paragraph') {
            <p><app-inline [items]="block.inlines" /></p>
          } @else if (block.ordered) {
            <ol>
              @for (item of block.items; track $index) {
                <li><app-inline [items]="item" /></li>
              }
            </ol>
          } @else {
            <ul>
              @for (item of block.items; track $index) {
                <li><app-inline [items]="item" /></li>
              }
            </ul>
          }
        }
      </div>

      @if (response().sources.length > 0) {
        <ul class="sources">
          @for (source of response().sources; track source.id) {
            <li>
              <button type="button" class="chip" (click)="store.openEvidence(source)">{{ source.id }}</button>
              {{ source.title }} · <span class="muted">{{ source.section }}</span>
            </li>
          }
        </ul>
      }

      <div class="feedback">
        @let voted = store.feedback()[response().answerId];
        <span class="muted">{{ i18n.t('answer.helpfulQuestion') }}</span>
        <button type="button" [class.active]="voted === true" [disabled]="voted !== undefined"
          (click)="store.sendFeedback(response().answerId, true)">{{ i18n.t('answer.helpful') }}</button>
        <button type="button" [class.active]="voted === false" [disabled]="voted !== undefined"
          (click)="store.sendFeedback(response().answerId, false)">{{ i18n.t('answer.notHelpful') }}</button>
      </div>
    </article>
  `,
})
export class AnswerComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);

  readonly response = input.required<ChatResponse>();

  protected readonly label = computed(() => this.i18n.t(STATUS_KEY[this.response().evidenceStatus]));
  protected readonly blocks = computed(() => parseAnswer(this.response().answer, this.response().sources));
}
