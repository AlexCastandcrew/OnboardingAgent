import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import { Language } from './i18n';
import {
  ChatRequest,
  ChatStreamEvent,
  DocumentView,
  DomainSummary,
  FeedbackRequest,
  HealthResponse,
  KnowledgeSummary,
  OnboardingRoute,
  ProjectSummary,
  Role,
} from './models';

/** What the app needs from the backend. The HTTP and mock implementations are swapped by environment.useMock. */
export abstract class OnboardingApi {
  abstract health(): Observable<HealthResponse>;
  abstract projects(): Observable<ProjectSummary[]>;
  abstract domains(projectId: string, language: Language): Observable<DomainSummary[]>;
  abstract knowledge(projectId: string): Observable<KnowledgeSummary>;
  abstract onboarding(projectId: string, role: Role, language: Language, domain?: string, initiative?: string): Observable<OnboardingRoute>;
  /**
   * Asks a question and streams what the agent does while it answers: activities first, then the response. A failure
   * (HTTP status before the stream, or an error event during it) arrives as an HttpErrorResponse. Unsubscribing cancels it.
   */
  abstract chatStream(request: ChatRequest): Observable<ChatStreamEvent>;
  abstract document(documentId: string): Observable<DocumentView>;
  abstract feedback(request: FeedbackRequest): Observable<void>;
}

export const ONBOARDING_API = new InjectionToken<OnboardingApi>('ONBOARDING_API');
