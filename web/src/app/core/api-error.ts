import { HttpErrorResponse } from '@angular/common/http';
import { Translate } from './i18n';

/** An API failure shown to the user. It is never presented as "no evidence". */
export interface ApiError {
  status: number;
  title: string;
  detail: string | null;
  /** Field name to messages, for a 400 validation problem. */
  fields: Record<string, string[]>;
  retryable: boolean;
  /** Seconds to wait before retrying, from Retry-After on a 429. */
  retryAfterSeconds: number | null;
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

const RETRYABLE = new Set([429, 502, 504]);

/** The title is written in the language of `t`; `detail` and `fields` come from the server as they are. */
export function toApiError(error: unknown, t: Translate): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, title: t('error.app'), detail: null, fields: {}, retryable: false, retryAfterSeconds: null };
  }

  const problem = (typeof error.error === 'object' && error.error !== null ? error.error : {}) as ProblemDetails;
  const retryAfter = Number(error.headers?.get('Retry-After'));

  return {
    status: error.status,
    title: titleFor(error.status, problem.title, t),
    detail: problem.detail ?? null,
    fields: problem.errors ?? {},
    retryable: RETRYABLE.has(error.status),
    retryAfterSeconds: error.status === 429 && Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null,
  };
}

function titleFor(status: number, serverTitle: string | undefined, t: Translate): string {
  switch (status) {
    case 0:
      return t('error.noConnection');
    case 400:
      return t('error.badRequest');
    case 401:
      return t('error.unauthorized');
    case 429:
      return t('error.tooMany');
    case 502:
      return t('error.model');
    case 504:
      return t('error.timeout');
    default:
      return serverTitle ?? t('error.other', { status });
  }
}
