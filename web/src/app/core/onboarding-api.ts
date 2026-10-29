import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import { Language } from './i18n';
import {
  ChatRequest,
  ChatResponse,
  DocumentView,
  DomainSummary,
  FeedbackRequest,
  HealthResponse,
  OnboardingRoute,
  ProjectSummary,
  Role,
} from './models';

/** What the app needs from the backend. The HTTP and mock implementations are swapped by environment.useMock. */
export abstract class OnboardingApi {
  abstract health(): Observable<HealthResponse>;
  abstract projects(): Observable<ProjectSummary[]>;
  abstract domains(projectId: string, language: Language): Observable<DomainSummary[]>;
  abstract onboarding(projectId: string, role: Role, language: Language, domain?: string, initiative?: string): Observable<OnboardingRoute>;
  abstract chat(request: ChatRequest): Observable<ChatResponse>;
  abstract document(documentId: string): Observable<DocumentView>;
  abstract feedback(request: FeedbackRequest): Observable<void>;
}

export const ONBOARDING_API = new InjectionToken<OnboardingApi>('ONBOARDING_API');
