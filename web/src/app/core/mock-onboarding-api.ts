import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, concat, concatMap, delay, from, of, throwError } from 'rxjs';
import { Language } from './i18n';
import {
  ChatActivity,
  ChatRequest,
  ChatResponse,
  ChatStreamEvent,
  DocumentView,
  DomainSummary,
  FeedbackRequest,
  HealthResponse,
  KnowledgeSummary,
  OnboardingRoute,
  OnboardingStep,
  ProjectSummary,
  Role,
} from './models';
import { OnboardingApi } from './onboarding-api';

const LATENCY_MS = 700;

const PROJECTS: ProjectSummary[] = [
  {
    projectId: 'htg',
    name: 'HTG',
    description: 'HTGPlus: plataforma de timecards, payroll y onboarding de Cast & Crew.',
    repoUrl: 'https://github.com/cast-and-crew/htg-server',
    snapshotDate: '2026-10-27',
  },
];

const DOMAINS: DomainSummary[] = [
  {
    domainId: 'rhaegal',
    name: 'Rhaegal',
    depth: 'overview',
    initiatives: [
      { initiativeId: 'H2G-6994', name: 'YEP' },
      { initiativeId: 'H2G-7016', name: 'Onboarding Documents Dashboard Fase 2' },
    ],
  },
  {
    domainId: 'silverwing',
    name: 'Silverwing',
    depth: 'full',
    initiatives: [
      { initiativeId: 'H2G-6627', name: 'Onboarding Documents Fase 1' },
      { initiativeId: 'H2G-6893', name: 'Excel Import: Executive Officer Payroll' },
    ],
  },
];

const DOCUMENT: DocumentView = {
  documentId: 'jira-h2g-7165',
  projectId: 'htg',
  title: 'H2G-7165 AI Data: read AI extraction responses from Kafka instead of Redis',
  sourceType: 'jira',
  url: null,
  path: 'jira/jira-h2g-7165.md',
  sourceVersion: 'snapshot-2026-10-27',
  sourceUpdatedAt: null,
  ingestedAt: '2026-10-28T20:00:00Z',
  domains: ['silverwing'],
  initiatives: ['H2G-6893'],
  content:
    '# H2G-7165 AI Data: read AI extraction responses from Kafka instead of Redis\n\n## Description\n\nThe AI Data screen shows AI extraction responses from a temporary Redis cache. Responses disappear after a fixed period.\n\nThe screen should retrieve the original message from Kafka, using the topic, partition and offset recorded when the mapper received it.',
  sections: [{ chunkId: 'jira-h2g-7165#description', section: 'Description' }],
};

function steps(role: Role, language: Language): OnboardingStep[] {
  const en = language === 'en';
  const emphasis: Record<Role, string> = en
    ? {
        developer: 'Clone, build and run the backend; learn the code standards.',
        qa: 'Prepare an environment to test in and learn how what you validate is versioned.',
        leader: 'Understand the components, the teams and their workstreams.',
      }
    : {
        developer: 'Clona, compila y ejecuta el backend; revisa los estándares de código.',
        qa: 'Prepara un entorno para probar y conoce cómo se versiona lo que validas.',
        leader: 'Entiende los componentes, los equipos y sus frentes.',
      };

  return [
    {
      step: 1, key: 'overview', title: 'Overview',
      goal: en ? 'Understand what HTG is and the teams that build it.' : 'Entender qué es HTG y los equipos que lo construyen.',
      status: 'ready', documentIds: ['teams'],
      documents: [{ documentId: 'teams', title: 'HTG teams and initiatives', sourceType: 'manual', url: null }],
      links: [], suggestedQuestions: [en ? 'Which teams work on HTG and on what workstreams?' : '¿Qué equipos trabajan en HTG y en qué frentes?'],
    },
    {
      step: 2, key: 'access', title: en ? 'Access' : 'Accesos',
      goal: en ? 'Request access to the applications: which ticket to create and who to ask.' : 'Pedir acceso a las aplicaciones: qué ticket crear y a quién.',
      status: 'pending', documentIds: ['confluence-access-request'], documents: [], links: [],
      suggestedQuestions: [en ? 'How do I request access to the HTG applications?' : '¿Cómo pido acceso a las aplicaciones de HTG?'],
    },
    {
      step: 3, key: 'local-setup', title: en ? 'Set up and test locally' : 'Configurar y probar en local', goal: emphasis[role],
      status: 'ready', documentIds: ['repo-handbook-readme'],
      documents: [{ documentId: 'repo-handbook-readme', title: 'H2G Developer Handbook', sourceType: 'repo', url: null }],
      links: [{ label: en ? 'htg-server repository' : 'Repositorio htg-server', url: 'https://github.com/cast-and-crew/htg-server' }],
      suggestedQuestions: [en ? 'How do I run HTG locally?' : '¿Cómo ejecuto HTG en local?'],
    },
    {
      step: 4, key: 'domain-questions', title: en ? 'Domain questions' : 'Preguntas del dominio',
      goal: en ? 'Questions to get started; the chat accepts any other.' : 'Preguntas para empezar; el chat acepta cualquier otra.',
      status: 'ready', documentIds: ['jira-h2g-7165'],
      documents: [{ documentId: 'jira-h2g-7165', title: DOCUMENT.title, sourceType: 'jira', url: null }],
      links: [],
      suggestedQuestions: en
        ? ['What changes in H2G-7165 on the AI Data screen?', 'Which tickets does the Excel Import have?']
        : ['¿Qué cambia H2G-7165 en la pantalla AI Data?', '¿Qué tickets tiene el Excel Import?'],
    },
  ];
}

function answerFor(request: ChatRequest): ChatResponse {
  const base = {
    answerId: crypto.randomUUID(),
    claims: [],
    diagnostics: { searchQuery: request.message, candidateCount: 12, contextCount: 4, reranked: true, generationAttempts: 1, elapsedMs: LATENCY_MS },
  };

  const en = request.language === 'en';
  if (/presupuesto|budget/i.test(request.message)) {
    return {
      ...base,
      answer: en ? 'The approved documentation does not cover this topic.' : 'La documentación aprobada no cubre este tema.',
      evidenceStatus: 'insufficient',
      sources: [],
    };
  }

  return {
    ...base,
    answer: en
      ? 'H2G-7165 changes the AI Data screen to read the extraction response from Kafka instead of Redis [S1]. ' +
        'The original message is retrieved with the topic, partition and offset the mapper logged when it received it [S1].'
      : 'H2G-7165 cambia la pantalla AI Data para leer la respuesta de extracción desde Kafka y no desde Redis [S1]. ' +
        'El mensaje original se recupera con el topic, la partición y el offset que el mapper registró al recibirlo [S1].',
    evidenceStatus: 'supported',
    sources: [
      {
        id: 'S1', documentId: 'jira-h2g-7165', chunkId: 'jira-h2g-7165#description', title: DOCUMENT.title, url: null,
        section: 'Description', sourceType: 'jira', sourceVersion: 'snapshot-2026-10-27', sourceUpdatedAt: null,
        excerpt: 'The screen should retrieve the original message from Kafka, using the topic, partition and offset recorded when the mapper received it.',
      },
    ],
  };
}

/** Emits each event, built at the moment it is emitted, with a short pause before it. */
function emit(steps: readonly (() => ChatStreamEvent)[]): Observable<ChatStreamEvent> {
  return from(steps).pipe(concatMap((build) => of(build()).pipe(delay(400))));
}

/** Fixtures for working without the backend. */
@Injectable()
export class MockOnboardingApi extends OnboardingApi {
  health(): Observable<HealthResponse> {
    return of<HealthResponse>({
      status: 'ok',
      index: { documents: 13, chunks: 17, lastIngestedAt: '2026-10-28T20:00:00Z' },
      models: { region: 'us-east-1', answerModelId: 'mock-answer', auxModelId: 'mock-aux', auth: 'mock', configured: true },
    }).pipe(delay(200));
  }

  projects(): Observable<ProjectSummary[]> {
    return of(PROJECTS).pipe(delay(200));
  }

  domains(_projectId: string, language: Language): Observable<DomainSummary[]> {
    const english = language === 'en';
    const names: Record<string, string> = { 'H2G-7016': 'Onboarding Documents Dashboard Phase 2', 'H2G-6627': 'Onboarding Documents Phase 1' };
    return of(DOMAINS.map((d) => ({
      ...d,
      initiatives: d.initiatives.map((i) => ({ ...i, name: english ? (names[i.initiativeId] ?? i.name) : i.name })),
    }))).pipe(delay(200));
  }

  knowledge(projectId: string): Observable<KnowledgeSummary> {
    return of<KnowledgeSummary>({
      projectId,
      documents: 21,
      passages: 231,
      lastIndexedAt: '2026-10-29T14:04:33Z',
      sourceTypes: [
        { sourceType: 'repo', documents: 9, passages: 120 },
        { sourceType: 'confluence', documents: 5, passages: 60 },
        { sourceType: 'jira', documents: 5, passages: 30 },
        { sourceType: 'manual', documents: 2, passages: 21 },
      ],
      repositories: [{ name: 'cast-and-crew/htg-server', url: 'https://github.com/cast-and-crew/htg-server', documents: 9, version: 'e570f4474d' }],
      teams: 3,
      initiatives: 9,
    }).pipe(delay(250));
  }

  onboarding(projectId: string, role: Role, language: Language, domain?: string, initiative?: string): Observable<OnboardingRoute> {
    return of<OnboardingRoute>({ projectId, role, domain: domain ?? null, initiative: initiative ?? null, steps: steps(role, language) }).pipe(delay(300));
  }

  /**
   * Scripted steps for working without the backend: the pauses between them are the mock's only timers, there is no
   * real work behind them. A message containing "[error]" fails at the ranking step with a 502.
   */
  chatStream(request: ChatRequest): Observable<ChatStreamEvent> {
    const none = /presupuesto|budget/i.test(request.message);
    const fails = request.message.includes('[error]');
    const step = (id: string, stepName: string, status: ChatActivity['status'], message: string, detail?: ChatActivity['detail']) => (): ChatStreamEvent => ({
      type: 'activity',
      activity: { id, step: stepName, status, message, timestamp: new Date().toISOString(), detail },
    });
    const sources = none ? [] : [
      { sourceType: 'confluence', documents: 2, passages: 3 },
      { sourceType: 'jira', documents: 1, passages: 2 },
      { sourceType: 'repo', documents: 1, passages: 1 },
    ];
    const candidates = sources.reduce((sum, s) => sum + s.passages, 0);

    const steps: (() => ChatStreamEvent)[] = [
      step('understand_question', 'understand_question', 'running', 'Understanding your question…'),
      step('understand_question', 'understand_question', 'completed', 'Question understood'),
      step('search_documents', 'search_documents', 'running', 'Searching the project documentation…'),
      step('search_documents', 'search_documents', 'completed', 'Found passages', { candidates, sources }),
      step('generate_answer-1', 'generate_answer', 'pending', 'Writing your answer'),
    ];
    if (candidates > 0) {
      steps.push(step('rank_passages', 'rank_passages', 'running', 'Ranking the relevant passages…'));
    }
    if (fails) {
      return concat(emit(steps), throwError(() => new HttpErrorResponse({ status: 502, headers: new HttpHeaders(), error: { title: 'The model could not answer. Try again.' } })));
    }

    if (candidates > 0) {
      steps.push(step('rank_passages', 'rank_passages', 'completed', 'Kept passages', { candidates, kept: 4, sources }));
    }
    steps.push(
      step('generate_answer-1', 'generate_answer', 'running', 'Writing your answer…', { attempt: 1 }),
      step('generate_answer-1', 'generate_answer', 'completed', 'Answer written', { attempt: 1 }),
      step('verify_citations-1', 'verify_citations', 'running', 'Verifying the citations…', { attempt: 1 }),
      step('verify_citations-1', 'verify_citations', 'completed', 'Citations verified', { attempt: 1, cited: none ? 0 : 1 }),
      (): ChatStreamEvent => ({ type: 'response', response: answerFor(request) }),
    );
    return emit(steps);
  }

  document(): Observable<DocumentView> {
    return of(DOCUMENT).pipe(delay(300));
  }

  feedback(_request: FeedbackRequest): Observable<void> {
    return of(undefined).pipe(delay(150));
  }
}
