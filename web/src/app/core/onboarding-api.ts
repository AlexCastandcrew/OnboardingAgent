import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import { Language } from './i18n';
import {
  AdminOverview,
  ChatRequest,
  ChatStreamEvent,
  DocumentView,
  DomainSummary,
  FeedbackRequest,
  HealthResponse,
  KnowledgeSummary,
  MeResponse,
  MyProgress,
  OnboardingRoute,
  ProjectSummary,
  Role,
  SaveProgressRequest,
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
  abstract me(): Observable<MeResponse>;
  abstract myProgress(): Observable<MyProgress>;
  /** Stores the steps the user marked as done and returns their updated progress. */
  abstract saveProgress(request: SaveProgressRequest): Observable<MyProgress>;
  /** Everyone's progress. Admins only: anyone else gets a 403. */
  abstract adminOverview(): Observable<AdminOverview>;
}

export const ONBOARDING_API = new InjectionToken<OnboardingApi>('ONBOARDING_API');
