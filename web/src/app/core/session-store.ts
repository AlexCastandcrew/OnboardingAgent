import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription, finalize, firstValueFrom } from 'rxjs';
import { ActivityView, applyActivity, failUnfinished } from './activity';
import { ApiError, toApiError } from './api-error';
import { ChatMessage, buildHistory } from './history';
import { I18n, Language } from './i18n';
import {
  AdminOverview,
  ChatSource,
  DocumentView,
  DomainSummary,
  HealthResponse,
  KnowledgeSummary,
  MAX_MESSAGE_CHARS,
  MeResponse,
  MyProgress,
  OnboardingRoute,
  ProjectSummary,
  Role,
} from './models';
import { ONBOARDING_API } from './onboarding-api';
import { ProgressState, computeProgress, contextKey, recommend, stepOfQuestion, toggleDone } from './progress';

const PROGRESS_STORAGE_KEY = 'onboarding.progress.v1';

function loadProgress(): ProgressState {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(PROGRESS_STORAGE_KEY) ?? '{}');
    return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as ProgressState) : {};
  } catch {
    return {};
  }
}

function saveProgress(state: ProgressState): void {
  try {
    localStorage.setItem(PROGRESS_STORAGE_KEY, JSON.stringify(state));
  } catch {
    // Storage full or blocked: progress then lasts only for this session.
  }
}

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
  /** What the agent is doing for the question in flight (or did before it failed). Moves into the answer when it arrives. */
  readonly activities = signal<ActivityView[]>([]);
  private lastQuestion: string | null = null;
  private inFlight: Subscription | null = null;
  /** Identifies the request whose result may still touch the state: a cancelled or replaced one must not. */
  private requestId = 0;

  readonly evidence = signal<EvidenceState | null>(null);
  readonly feedback = signal<Record<string, boolean>>({});

  /** What the index holds for the selected project; null while loading or when the call failed (the card says so). */
  readonly knowledge = signal<KnowledgeSummary | null>(null);
  readonly knowledgeFailed = signal(false);

  /** Who is signed in. Null until known or when the call failed: then there is no admin view. */
  readonly me = signal<MeResponse | null>(null);
  /** The server's copy of the user's progress: level, day, questions asked (a count) and pace. */
  readonly myProgress = signal<MyProgress | null>(null);
  readonly view = signal<'chat' | 'admin'>('chat');
  readonly adminOverview = signal<AdminOverview | null>(null);
  readonly adminLoading = signal(false);
  readonly adminError = signal<ApiError | null>(null);

  private readonly progressState = signal<ProgressState>(loadProgress());
  private readonly doneKeys = computed(() => this.progressState()[this.progressKey()] ?? []);
  private readonly progressKey = computed(() => contextKey(this.projectId() ?? '', this.role(), this.domain(), this.initiative()));
  readonly doneSteps = this.doneKeys;
  readonly progress = computed(() => computeProgress(this.route(), this.doneKeys()));
  readonly recommendations = computed(() => recommend(this.route(), this.doneKeys()));

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
      void this.loadMe();
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

    void this.loadKnowledge(projectId);
    await this.reload();
  }

  private async loadMe(): Promise<void> {
    try {
      this.me.set(await firstValueFrom(this.api.me()));
      this.myProgress.set(await firstValueFrom(this.api.myProgress()));
    } catch {
      // Without the server's copy the browser's own progress still works; there is just no level, pace or admin view.
    }
  }

  /** Tells the server which steps are done, so the level, day and pace (and the admin view) follow. A failure is silent. */
  private async syncProgress(): Promise<void> {
    const projectId = this.projectId();
    if (!projectId || this.route() === null) {
      return;
    }

    try {
      this.myProgress.set(await firstValueFrom(this.api.saveProgress({
        projectId,
        role: this.role(),
        domain: this.domain() ?? undefined,
        initiative: this.initiative() ?? undefined,
        doneSteps: [...this.doneKeys()],
        totalSteps: this.progress().total,
      })));
    } catch {
      // The browser's copy is still right; the server's copy catches up on the next change.
    }
  }

  private async refreshMyProgress(): Promise<void> {
    try {
      this.myProgress.set(await firstValueFrom(this.api.myProgress()));
    } catch {
      // Keep the last known values.
    }
  }

  async openAdmin(): Promise<void> {
    this.view.set('admin');
    await this.loadAdmin();
  }

  closeAdmin(): void {
    this.view.set('chat');
  }

  async loadAdmin(): Promise<void> {
    this.adminLoading.set(true);
    this.adminError.set(null);
    try {
      this.adminOverview.set(await firstValueFrom(this.api.adminOverview()));
    } catch (error) {
      this.adminError.set(toApiError(error, this.i18n.t));
    } finally {
      this.adminLoading.set(false);
    }
  }

  private async loadKnowledge(projectId: string): Promise<void> {
    this.knowledge.set(null);
    this.knowledgeFailed.set(false);
    try {
      const knowledge = await firstValueFrom(this.api.knowledge(projectId));
      if (this.projectId() === projectId) {
        this.knowledge.set(knowledge);
      }
    } catch {
      if (this.projectId() === projectId) {
        this.knowledgeFailed.set(true);
      }
    }
  }

  toggleStepDone(stepKey: string): void {
    this.setProgress(toggleDone(this.progressState(), this.progressKey(), stepKey));
  }

  private setProgress(state: ProgressState): void {
    this.progressState.set(state);
    saveProgress(state);
    void this.syncProgress();
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
    void this.syncProgress();
  }

  /** Clears the conversation and any open evidence; the project, role, team, initiative and language stay. */
  newConversation(): void {
    this.resetConversation();
  }

  /** Stops the question in flight, if any: closes its stream (the server stops its work) and forgets its activities. */
  private cancelInFlight(): void {
    this.requestId++;
    this.inFlight?.unsubscribe();
    this.inFlight = null;
    this.asking.set(false);
    this.askingSince.set(null);
    this.activities.set([]);
  }

  private resetConversation(): void {
    this.cancelInFlight();
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

    // Asking a question the route suggests for a step counts as doing that step.
    const step = stepOfQuestion(this.route(), text.trim());
    if (step && !this.doneKeys().includes(step.key)) {
      this.toggleStepDone(step.key);
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

    const id = ++this.requestId;
    this.lastQuestion = message;
    this.chatError.set(null);
    this.activities.set([]);
    this.asking.set(true);
    this.askingSince.set(Date.now());

    await new Promise<void>((resolve) => {
      // finalize also runs when the stream is cancelled, so this promise always settles.
      this.inFlight = this.api.chatStream({
        projectId,
        domain: this.domain() ?? undefined,
        initiative: this.initiative() ?? undefined,
        role: this.role(),
        language: this.i18n.language(),
        message,
        history,
      }).pipe(finalize(resolve)).subscribe({
        next: (event) => {
          if (id !== this.requestId) {
            return;
          }

          if (event.type === 'activity') {
            this.activities.update((list) => applyActivity(list, event.activity));
          } else {
            this.messages.update((m) => [...m, { kind: 'assistant', response: event.response, activities: this.activities() }]);
            this.activities.set([]);
            this.lastQuestion = null;
            void this.refreshMyProgress();
          }
        },
        error: (error) => {
          if (id === this.requestId) {
            this.activities.update(failUnfinished);
            this.chatError.set(toApiError(error, this.i18n.t));
          }
        },
      });
    });

    if (id === this.requestId) {
      this.inFlight = null;
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
