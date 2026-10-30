import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DemoAuth } from '../../core/demo-auth';
import { I18n, LANGUAGES } from '../../core/i18n';
import { SessionStore } from '../../core/session-store';

/** First page of the demo: a simulated sign-in (see DemoAuth). */
@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <main class="login-page">
      <div class="login-language" role="group" [attr.aria-label]="i18n.t('lang.label')">
        @for (option of languages; track option.id) {
          <button
            type="button"
            class="login-lang-button"
            [class.active]="i18n.language() === option.id"
            [attr.aria-pressed]="i18n.language() === option.id"
            (click)="store.setLanguage(option.id)">{{ option.label }}</button>
        }
      </div>

      <form class="login-card" (ngSubmit)="submit()">
        <img src="moltres.png" width="88" height="88" alt="Moltres Agent" />
        <h1>Moltres Agent</h1>
        <p class="login-powered">Powered by <strong>Cast &amp; Crew</strong></p>
        <p class="login-subtitle">{{ i18n.t('login.subtitle') }}</p>

        <label>
          <span>{{ i18n.t('login.email') }}</span>
          <input type="email" name="email" autocomplete="username" required [ngModel]="email()" (ngModelChange)="email.set($event); failed.set(false)" />
        </label>
        <label>
          <span>{{ i18n.t('login.password') }}</span>
          <input type="password" name="password" autocomplete="current-password" required [ngModel]="password()" (ngModelChange)="password.set($event); failed.set(false)" />
        </label>

        @if (failed()) {
          <p class="login-error" role="alert">{{ i18n.t('login.error') }}</p>
        }

        <button type="submit" class="btn-primary login-submit" [disabled]="email().trim() === '' || password() === ''">{{ i18n.t('login.submit') }}</button>
        <p class="muted login-note">{{ i18n.t('login.note') }}</p>
      </form>
    </main>
  `,
})
export class LoginComponent {
  private readonly auth = inject(DemoAuth);
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly languages = LANGUAGES;

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly failed = signal(false);

  protected submit(): void {
    if (!this.auth.signIn(this.email(), this.password())) {
      this.failed.set(true);
      this.password.set('');
    }
  }
}
