import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, Subscriber } from 'rxjs';
import { Language } from './i18n';
import { parseSseFrames } from './sse';
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
  ProjectSummary,
  Role,
} from './models';
import { OnboardingApi } from './onboarding-api';

interface StreamError {
  status: number;
  title: string;
  detail: string | null;
}

async function readChatStream(request: ChatRequest, signal: AbortSignal, subscriber: Subscriber<ChatStreamEvent>): Promise<void> {
  try {
    const response = await fetch('/api/chat/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify(request),
      signal,
    });
    if (!response.ok) {
      throw await httpError(response);
    }

    const reader = response.body?.getReader();
    if (!reader) {
      throw new HttpErrorResponse({ status: 0 });
    }

    const decoder = new TextDecoder();
    let buffer = '';
    let answered = false;
    for (;;) {
      const { done, value } = await reader.read();
      buffer += decoder.decode(value, { stream: !done });
      const parsed = parseSseFrames(done ? `${buffer}\n\n` : buffer);
      buffer = parsed.rest;

      for (const frame of parsed.frames) {
        if (frame.event === 'activity') {
          subscriber.next({ type: 'activity', activity: JSON.parse(frame.data) as ChatActivity });
        } else if (frame.event === 'response') {
          answered = true;
          subscriber.next({ type: 'response', response: JSON.parse(frame.data) as ChatResponse });
        } else if (frame.event === 'error') {
          const failure = JSON.parse(frame.data) as StreamError;
          throw new HttpErrorResponse({ status: failure.status, error: { title: failure.title, detail: failure.detail } });
        }
      }

      if (done) {
        break;
      }
    }

    if (!answered) {
      // The connection closed with no answer and no error event: treat it as a lost connection.
      throw new HttpErrorResponse({ status: 0 });
    }

    subscriber.complete();
  } catch (error) {
    if (!signal.aborted) {
      subscriber.error(error instanceof HttpErrorResponse ? error : new HttpErrorResponse({ status: 0 }));
    }
  }
}

async function httpError(response: Response): Promise<HttpErrorResponse> {
  let body: unknown = null;
  try {
    body = await response.json();
  } catch {
    // Not JSON: the status alone says enough.
  }

  const headers = new HttpHeaders();
  const retryAfter = response.headers.get('Retry-After');
  return new HttpErrorResponse({ status: response.status, error: body, headers: retryAfter ? headers.set('Retry-After', retryAfter) : headers });
}

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

  knowledge(projectId: string): Observable<KnowledgeSummary> {
    return this.http.get<KnowledgeSummary>(`/api/projects/${encodeURIComponent(projectId)}/knowledge`);
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

  /**
   * Server-Sent Events over POST. HttpClient cannot read a response as it arrives, so this uses fetch; the dev proxy adds
   * the bearer token to this path like to any other /api call. Errors are shaped as HttpErrorResponse so the existing
   * error mapping applies, whether they come as an HTTP status before the stream or as an error event inside it.
   */
  chatStream(request: ChatRequest): Observable<ChatStreamEvent> {
    return new Observable<ChatStreamEvent>((subscriber) => {
      const controller = new AbortController();
      void readChatStream(request, controller.signal, subscriber);
      return () => controller.abort();
    });
  }

  document(documentId: string): Observable<DocumentView> {
    return this.http.get<DocumentView>(`/api/documents/${encodeURIComponent(documentId)}`);
  }

  feedback(request: FeedbackRequest): Observable<void> {
    return this.http.post<void>('/api/feedback', request);
  }
}
