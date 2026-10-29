import { Component, inject } from '@angular/core';
import { I18n, LANGUAGES } from './core/i18n';
import { SessionStore } from './core/session-store';
import { ChatComponent } from './features/chat/chat';
import { EvidenceComponent } from './features/evidence/evidence';
import { RouteComponent } from './features/route/route';
import { SelectionComponent } from './features/selection/selection';

@Component({
  selector: 'app-root',
  imports: [SelectionComponent, RouteComponent, ChatComponent, EvidenceComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly languages = LANGUAGES;

  constructor() {
    void this.store.init();
  }
}
