import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
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
import { OnboardingApi } from './onboarding-api';

@Injectable()
export class HttpOnboardingApi extends OnboardingApi {
  private readonly http = inject(HttpClient);

  health(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>('/api/health');
  }

  projects(): Observable<ProjectSummary[]> {
    return this.http.get<ProjectSummary[]>('/api/projects');
  }

  domains(projectId: string, language: Language): Observable<DomainSummary[]> {
    return this.http.get<DomainSummary[]>(`/api/projects/${encodeURIComponent(projectId)}/domains`, {
      params: new HttpParams().set('language', language),
    });
  }

  onboarding(projectId: string, role: Role, language: Language, domain?: string, initiative?: string): Observable<OnboardingRoute> {
    let params = new HttpParams().set('language', language);
    if (domain) {
      params = params.set('domain', domain);
    }
    if (initiative) {
      params = params.set('initiative', initiative);
    }

    return this.http.get<OnboardingRoute>(
      `/api/onboarding/${encodeURIComponent(projectId)}/${encodeURIComponent(role)}`,
      { params },
    );
  }

  chat(request: ChatRequest): Observable<ChatResponse> {
    return this.http.post<ChatResponse>('/api/chat', request);
  }

  document(documentId: string): Observable<DocumentView> {
    return this.http.get<DocumentView>(`/api/documents/${encodeURIComponent(documentId)}`);
  }

  feedback(request: FeedbackRequest): Observable<void> {
    return this.http.post<void>('/api/feedback', request);
  }
}
