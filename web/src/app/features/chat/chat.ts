import { Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { I18n } from '../../core/i18n';
import { MAX_MESSAGE_CHARS } from '../../core/models';
import { SessionStore } from '../../core/session-store';
import { IconComponent } from '../../shared/icon';
import { ActivityFeedComponent } from '../activity/activity-feed';
import { AnswerComponent } from '../answer/answer';
import { OnboardingProgressComponent, ProjectGlanceComponent, RecommendationsComponent } from '../home/home-cards';
import { SuggestionsComponent } from '../suggestions/suggestions';

@Component({
  selector: 'app-chat',
  imports: [
    FormsModule,
    ActivityFeedComponent,
    AnswerComponent,
    SuggestionsComponent,
    IconComponent,
    OnboardingProgressComponent,
    RecommendationsComponent,
    ProjectGlanceComponent,
  ],
  template: `
    <section class="chat" [class.empty]="isEmpty()" [attr.aria-label]="i18n.t('chat.heading')">
      @if (isEmpty()) {
        <div class="welcome">
          <span class="welcome-mark"><app-icon name="sparkles" /></span>
          <h1>{{ i18n.t('welcome.title') }}</h1>
          <p class="welcome-subtitle">{{ i18n.t('welcome.subtitle') }}</p>
          <p class="welcome-description">{{ i18n.t('welcome.description') }}</p>
        </div>
        <app-onboarding-progress />
      } @else {
        <div class="conversation" aria-live="polite">
          @for (message of store.messages(); track $index) {
            @if (message.kind === 'user') {
              <div class="turn user-turn"><p class="user-bubble">{{ message.text }}</p></div>
            } @else {
              <div class="turn assistant-turn">
                <span class="avatar" [attr.title]="i18n.t('chat.assistant')"><app-icon name="sparkles" /></span>
                <div class="assistant-stack">
                  @if (message.activities.length > 0) {
                    <app-activity-feed [activities]="message.activities" />
                  }
                  <app-answer [response]="message.response" />
                </div>
              </div>
            }
          }

          @if (store.asking() || store.activities().length > 0) {
            <div class="turn assistant-turn" role="status">
              <span class="avatar"><app-icon name="sparkles" /></span>
              <div class="assistant-stack">
                <app-activity-feed
                  [activities]="store.activities()"
                  [running]="store.asking()"
                  [errored]="!store.asking() && store.chatError() !== null"
                  [elapsed]="elapsed()" />
              </div>
            </div>
          }

          @if (store.chatError(); as error) {
            <div class="chat-error" role="alert">
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
                <button type="button" class="btn-primary" (click)="store.retry()" [disabled]="store.asking()">{{ i18n.t('chat.retry') }}</button>
              }
            </div>
          }
        </div>
      }

      <div class="dock">
        <form class="composer" (ngSubmit)="submit()">
          <textarea
            name="message"
            rows="2"
            [maxlength]="max"
            [placeholder]="i18n.t('chat.placeholder')"
            [ngModel]="draft()"
            (ngModelChange)="draft.set($event)"
            (keydown.enter)="onEnter($event)"></textarea>
          <div class="composer-bar">
            <button
              type="button"
              class="icon-button"
              [attr.aria-label]="i18n.t('chat.newConversation')"
              [title]="i18n.t('chat.newConversation')"
              [disabled]="store.asking() || (store.messages().length === 0 && !store.chatError())"
              (click)="store.newConversation()">
              <app-icon name="plus" />
            </button>
            @if (draft().length > 0) {
              <span class="counter">{{ draft().length }} / {{ max }}</span>
            }
            <button
              type="submit"
              class="send-button"
              [attr.aria-label]="i18n.t('chat.send')"
              [title]="i18n.t('chat.send')"
              [disabled]="!store.canAsk(draft())">
              <app-icon name="arrow-up" />
            </button>
          </div>
        </form>
      </div>

      @if (isEmpty()) {
        <app-suggestions />
        <app-recommendations />
        <app-project-glance />
      }

      <p class="disclaimer">{{ i18n.t('app.disclaimer') }}</p>
    </section>
  `,
})
export class ChatComponent implements OnDestroy {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly max = MAX_MESSAGE_CHARS;
  protected readonly draft = signal('');

  /** No question yet: the welcome and the starter questions are shown. An error or an answer in flight is not empty. */
  protected readonly isEmpty = computed(() => this.store.messages().length === 0 && !this.store.asking() && !this.store.chatError());

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
