import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from './api-error';
import { ChatMessage, buildHistory } from './history';
import { I18n, Language } from './i18n';
import {
  ChatSource,
  DocumentView,
  DomainSummary,
  HealthResponse,
  MAX_MESSAGE_CHARS,
  OnboardingRoute,
  ProjectSummary,
  Role,
} from './models';
import { ONBOARDING_API } from './onboarding-api';

export interface InitiativeOption {
  initiativeId: string;
  name: string;
  domainId: string;
  domainName: string;
}

export interface EvidenceState {
  source: ChatSource;
  document: DocumentView | null;
  loading: boolean;
  error: ApiError | null;
}

@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly api = inject(ONBOARDING_API);
  private readonly i18n = inject(I18n);

  readonly health = signal<HealthResponse | null>(null);
  readonly projects = signal<ProjectSummary[]>([]);
  readonly domains = signal<DomainSummary[]>([]);

  readonly projectId = signal<string | null>(null);
  readonly role = signal<Role>('developer');
  readonly domain = signal<string | null>(null);
  readonly initiative = signal<string | null>(null);

  readonly route = signal<OnboardingRoute | null>(null);
  readonly routeError = signal<ApiError | null>(null);
  readonly loadingRoute = signal(false);

  readonly messages = signal<ChatMessage[]>([]);
  readonly asking = signal(false);
  readonly askingSince = signal<number | null>(null);
  readonly chatError = signal<ApiError | null>(null);
  private lastQuestion: string | null = null;

  readonly evidence = signal<EvidenceState | null>(null);
  readonly feedback = signal<Record<string, boolean>>({});

  /** With a team selected, that team's initiatives; with "General", the initiatives of every team. */
  readonly initiatives = computed<InitiativeOption[]>(() =>
    this.domains()
      .filter((d) => this.domain() === null || d.domainId === this.domain())
      .flatMap((d) => d.initiatives.map((i) => ({ ...i, domainId: d.domainId, domainName: d.name }))));
  readonly healthWarning = computed(() => {
    const h = this.health();
    return h !== null && (h.status !== 'ok' || !h.models.configured);
  });

  async init(): Promise<void> {
    this.api.health().subscribe({ next: (h) => this.health.set(h), error: () => this.health.set(null) });

    try {
      const projects = await firstValueFrom(this.api.projects());
      this.projects.set(projects);
      if (projects.length > 0) {
        await this.selectProject(projects[0].projectId);
      }
    } catch (error) {
      this.routeError.set(toApiError(error, this.i18n.t));
    }
  }

  async selectProject(projectId: string): Promise<void> {
    this.projectId.set(projectId);
    this.domain.set(null);
    this.initiative.set(null);
    try {
      this.domains.set(await firstValueFrom(this.api.domains(projectId, this.i18n.language())));
    } catch (error) {
      this.routeError.set(toApiError(error, this.i18n.t));
      return;
    }

    await this.reload();
  }

  async selectRole(role: Role): Promise<void> {
    this.role.set(role);
    await this.reload();
  }

  async selectDomain(domainId: string | null): Promise<void> {
    this.domain.set(domainId);
    this.initiative.set(null);
    await this.reload();
  }

  /** An initiative belongs to one team, so choosing it under "General" also selects that team. */
  async selectInitiative(initiativeId: string | null): Promise<void> {
    const option = this.initiatives().find((i) => i.initiativeId === initiativeId);
    if (option) {
      this.domain.set(option.domainId);
    }

    this.initiative.set(initiativeId);
    await this.reload();
  }

  /**
   * Switches the language of the UI, the onboarding route, the initiative names and the next answers. The conversation
   * stays: earlier answers keep the language they were written in.
   */
  async setLanguage(language: Language): Promise<void> {
    if (language === this.i18n.language()) {
      return;
    }

    const projectId = this.projectId();
    if (projectId) {
      this.loadingRoute.set(true);
      try {
        const [domains, route] = await Promise.all([
          firstValueFrom(this.api.domains(projectId, language)),
          firstValueFrom(this.api.onboarding(projectId, this.role(), language, this.domain() ?? undefined, this.initiative() ?? undefined)),
        ]);
        this.domains.set(domains);
        this.route.set(route);
        this.routeError.set(null);
      } catch (error) {
        // The UI language changes only together with its content: on a failure both stay as they were, never a mix.
        this.routeError.set(toApiError(error, this.i18n.t));
        return;
      } finally {
        this.loadingRoute.set(false);
      }
    }

    this.i18n.set(language);
  }

  /** A new selection starts a new conversation and a new route. */
  private async reload(): Promise<void> {
    this.resetConversation();
    await this.loadRoute();
  }

  private async loadRoute(): Promise<void> {
    const projectId = this.projectId();
    if (!projectId) {
      return;
    }

    this.loadingRoute.set(true);
    this.routeError.set(null);
    try {
      this.route.set(await firstValueFrom(
        this.api.onboarding(projectId, this.role(), this.i18n.language(), this.domain() ?? undefined, this.initiative() ?? undefined)));
    } catch (error) {
      this.route.set(null);
      this.routeError.set(toApiError(error, this.i18n.t));
    } finally {
      this.loadingRoute.set(false);
    }
  }

  private resetConversation(): void {
    this.messages.set([]);
    this.chatError.set(null);
    this.lastQuestion = null;
    this.evidence.set(null);
    this.feedback.set({});
  }

  /** True when the text can be sent: not empty, within the limit and no answer in flight. */
  canAsk(text: string): boolean {
    const trimmed = text.trim();
    return trimmed.length > 0 && trimmed.length <= MAX_MESSAGE_CHARS && !this.asking();
  }

  async ask(text: string): Promise<void> {
    if (!this.canAsk(text)) {
      return;
    }

    const history = buildHistory(this.messages());
    this.messages.update((m) => [...m, { kind: 'user', text: text.trim() }]);
    await this.send(text.trim(), history);
  }

  /** Repeats the last question without adding it to the conversation a second time. */
  async retry(): Promise<void> {
    if (this.lastQuestion === null || this.asking()) {
      return;
    }

    await this.send(this.lastQuestion, buildHistory(this.messages().slice(0, -1)));
  }

  private async send(message: string, history: ReturnType<typeof buildHistory>): Promise<void> {
    const projectId = this.projectId();
    if (!projectId) {
      return;
    }

    this.lastQuestion = message;
    this.chatError.set(null);
    this.asking.set(true);
    this.askingSince.set(Date.now());
    try {
      const response = await firstValueFrom(this.api.chat({
        projectId,
        domain: this.domain() ?? undefined,
        initiative: this.initiative() ?? undefined,
        role: this.role(),
        language: this.i18n.language(),
        message,
        history,
      }));
      this.messages.update((m) => [...m, { kind: 'assistant', response }]);
      this.lastQuestion = null;
    } catch (error) {
      this.chatError.set(toApiError(error, this.i18n.t));
    } finally {
      this.asking.set(false);
      this.askingSince.set(null);
    }
  }

  async openEvidence(source: ChatSource): Promise<void> {
    this.evidence.set({ source, document: null, loading: true, error: null });
    try {
      const document = await firstValueFrom(this.api.document(source.documentId));
      this.evidence.set({ source, document, loading: false, error: null });
    } catch (error) {
      this.evidence.set({ source, document: null, loading: false, error: toApiError(error, this.i18n.t) });
    }
  }

  closeEvidence(): void {
    this.evidence.set(null);
  }

  async sendFeedback(answerId: string, helpful: boolean): Promise<void> {
    this.feedback.update((f) => ({ ...f, [answerId]: helpful }));
    try {
      await firstValueFrom(this.api.feedback({ answerId, helpful }));
    } catch {
      this.feedback.update(({ [answerId]: _removed, ...rest }) => rest);
    }
  }
}
