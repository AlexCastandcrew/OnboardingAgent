import { Component, effect, inject, signal, untracked } from '@angular/core';
import { I18n, LANGUAGES } from './core/i18n';
import { SessionStore } from './core/session-store';
import { AdminViewComponent } from './features/admin/admin-view';
import { ChatComponent } from './features/chat/chat';
import { EvidenceComponent } from './features/evidence/evidence';
import { RouteComponent } from './features/route/route';
import { SelectionComponent } from './features/selection/selection';
import { IconComponent } from './shared/icon';

@Component({
  selector: 'app-root',
  imports: [SelectionComponent, RouteComponent, ChatComponent, AdminViewComponent, EvidenceComponent, IconComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
  host: { '(document:keydown.escape)': 'drawerOpen.set(false)' },
})
export class App {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly languages = LANGUAGES;

  /** On small screens the context and the route live in a drawer that this opens and closes. */
  protected readonly drawerOpen = signal(false);

  constructor() {
    void this.store.init();

    // A new selection or a new question means the user is done with the drawer and wants to see the answer.
    effect(() => {
      this.store.route();
      this.store.messages();
      untracked(() => this.drawerOpen.set(false));
    });
  }
}
