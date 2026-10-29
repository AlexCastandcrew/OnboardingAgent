import { Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { I18n } from '../../core/i18n';
import { MAX_MESSAGE_CHARS } from '../../core/models';
import { SessionStore } from '../../core/session-store';
import { AnswerComponent } from '../answer/answer';

@Component({
  selector: 'app-chat',
  imports: [FormsModule, AnswerComponent],
  template: `
    <section class="panel chat" aria-label="Chat">
      <div class="panel-heading"><h2>{{ i18n.t('chat.heading') }}</h2></div>
      <div class="panel-body">

      <div class="messages" aria-live="polite">
        @if (store.messages().length === 0 && !store.asking()) {
          <p class="muted">{{ i18n.t('chat.empty') }}</p>
        }

        @for (message of store.messages(); track $index) {
          @if (message.kind === 'user') {
            <p class="bubble user">{{ message.text }}</p>
          } @else {
            <app-answer [response]="message.response" />
          }
        }

        @if (store.asking()) {
          <p class="muted" role="status">{{ i18n.t('chat.searching') }} {{ elapsed() }} s</p>
        }

        @if (store.chatError(); as error) {
          <div class="error" role="alert">
            <p>{{ error.title }}</p>
            @if (error.detail) {
              <p class="muted">{{ error.detail }}</p>
            }
            @for (field of fieldMessages(error.fields); track field) {
              <p>{{ field }}</p>
            }
            @if (error.retryAfterSeconds) {
              <p class="muted">{{ i18n.t('chat.retryIn', { n: error.retryAfterSeconds }) }}</p>
            }
            @if (error.retryable) {
              <button type="button" (click)="store.retry()" [disabled]="store.asking()">{{ i18n.t('chat.retry') }}</button>
            }
          </div>
        }
      </div>

      <form class="composer" (ngSubmit)="submit()">
        <textarea
          name="message"
          rows="2"
          [maxlength]="max"
          [placeholder]="i18n.t('chat.placeholder')"
          [ngModel]="draft()"
          (ngModelChange)="draft.set($event)"
          (keydown.enter)="onEnter($event)"></textarea>
        <button type="submit" [disabled]="!store.canAsk(draft())">{{ i18n.t('chat.send') }}</button>
        <span class="muted counter">{{ draft().length }} / {{ max }}</span>
      </form>
      </div>
    </section>
  `,
})
export class ChatComponent implements OnDestroy {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly max = MAX_MESSAGE_CHARS;
  protected readonly draft = signal('');

  private readonly now = signal(Date.now());
  private readonly timer = setInterval(() => this.now.set(Date.now()), 500);
  protected readonly elapsed = computed(() => {
    const since = this.store.askingSince();
    return since === null ? 0 : Math.max(0, Math.round((this.now() - since) / 1000));
  });

  constructor() {
    // A new selection starts a new conversation, so an unsent draft is dropped with it.
    effect(() => {
      this.store.route();
      this.draft.set('');
    });
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected submit(): void {
    const text = this.draft();
    if (!this.store.canAsk(text)) {
      return;
    }

    this.draft.set('');
    void this.store.ask(text);
  }

  /** Enter sends; Shift+Enter adds a line. */
  protected onEnter(event: Event): void {
    if ((event as KeyboardEvent).shiftKey) {
      return;
    }

    event.preventDefault();
    this.submit();
  }

  protected fieldMessages(fields: Record<string, string[]>): string[] {
    return Object.values(fields).flat();
  }
}
