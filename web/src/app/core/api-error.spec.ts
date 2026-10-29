import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { toApiError as toError } from './api-error';
import { TRANSLATIONS, Translate } from './i18n';

const failure = (status: number, error: unknown = null, headers = new HttpHeaders()) =>
  new HttpErrorResponse({ status, error, headers });

const spanish: Translate = (key) => TRANSLATIONS.es[key];
const toApiError = (error: unknown) => toError(error, spanish);

describe('toApiError', () => {
  it('toApiError_ServerErrorOrTimeoutOrThrottle_IsRetryable', () => {
    expect([502, 504, 429].map((s) => toApiError(failure(s)).retryable)).toEqual([true, true, true]);
  });

  it('toApiError_ValidationOrAuthFailure_IsNotRetryable', () => {
    expect([400, 401].map((s) => toApiError(failure(s)).retryable)).toEqual([false, false]);
  });

  it('toApiError_ThrottleWithRetryAfter_ExposesTheSeconds', () => {
    const error = toApiError(failure(429, null, new HttpHeaders({ 'Retry-After': '12' })));

    expect(error.retryAfterSeconds).toBe(12);
  });

  it('toApiError_ValidationProblem_KeepsTheFieldMessages', () => {
    const error = toApiError(failure(400, { errors: { message: ['Message is required.'] } }));

    expect(error.fields['message']).toEqual(['Message is required.']);
  });

  it('toApiError_EnglishTranslator_WritesTheTitleInEnglish', () => {
    const english: Translate = (key, params) => TRANSLATIONS.en[key].replace('{status}', String(params?.['status'] ?? ''));

    expect(toError(failure(429), english).title).toBe(TRANSLATIONS.en['error.tooMany']);
    expect(toError(failure(418), english).title).toContain('418');
  });

  it('toApiError_NoConnection_IsNotShownAsMissingEvidence', () => {
    const error = toApiError(failure(0));

    expect(error.title).toContain('API');
  });
});
