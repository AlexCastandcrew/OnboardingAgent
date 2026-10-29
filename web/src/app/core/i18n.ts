import { Injectable, signal } from '@angular/core';

export type Language = 'es' | 'en';

export const LANGUAGES: readonly { id: Language; label: string }[] = [
  { id: 'es', label: 'ES' },
  { id: 'en', label: 'EN' },
];

const es = {
  'app.subtitle': 'Respuestas con citas sobre un corpus aprobado. Demo de hackathon: no mide tiempo de onboarding ahorrado.',
  'app.healthWarning': 'La API está degradada o sin modelos configurados: las preguntas pueden fallar.',
  'lang.label': 'Idioma',
  'sel.heading': 'Contexto',
  'sel.project': 'Proyecto',
  'sel.role': 'Rol',
  'sel.domain': 'Dominio (equipo)',
  'sel.initiative': 'Iniciativa',
  'sel.general': 'General',
  'sel.all': 'Todas',
  'sel.overviewOnly': 'solo overview',
  'route.heading': 'Ruta de onboarding',
  'route.loading': 'Cargando la ruta…',
  'route.ready': 'Listo',
  'route.pending': 'Pendiente',
  'route.pendingText': 'Todavía no hay documentación aprobada para este paso.',
  'chat.heading': 'Pregunta sobre el proyecto',
  'chat.empty': 'Elige una pregunta sugerida de la ruta o escribe la tuya.',
  'chat.searching': 'Buscando en la documentación…',
  'chat.retryIn': 'Reintenta en {n} s.',
  'chat.retry': 'Reintentar',
  'chat.placeholder': 'Escribe tu pregunta…',
  'chat.send': 'Enviar',
  'answer.supported': 'Respaldada por la documentación',
  'answer.partial': 'Parcial: la documentación cubre solo una parte',
  'answer.insufficient': 'Evidencia insuficiente',
  'answer.helpfulQuestion': '¿Te sirvió?',
  'answer.helpful': 'Útil',
  'answer.notHelpful': 'No útil',
  'evidence.label': 'Evidencia',
  'evidence.close': 'Cerrar evidencia',
  'evidence.section': 'sección',
  'evidence.version': 'versión',
  'evidence.excerpt': 'Fragmento citado',
  'evidence.openSource': 'Abrir la fuente original',
  'evidence.loading': 'Cargando el documento…',
  'evidence.fullDocument': 'Documento completo',
  'evidence.path': 'Ruta en el corpus',
  'evidence.versionLabel': 'Versión',
  'evidence.sourceDate': 'Fecha de origen',
  'evidence.notRecorded': 'no registrada',
  'evidence.ingestedDate': 'Fecha de ingesta',
  'evidence.snapshotNote':
    'Es una copia (snapshot) importada al corpus. Para los documentos de htg-server la fecha de origen puede ser la de importación, no la del último cambio en git.',
  'error.app': 'Algo salió mal en la aplicación.',
  'error.noConnection': 'No hay conexión con la API. Revisa que esté corriendo.',
  'error.badRequest': 'La solicitud no es válida.',
  'error.unauthorized': 'El token de acceso falló o venció. Reinicia scripts/start-web.ps1 para renovarlo.',
  'error.tooMany': 'Demasiadas preguntas seguidas. Espera un momento y reintenta.',
  'error.model': 'El modelo no pudo responder. Reintenta.',
  'error.timeout': 'La respuesta tardó demasiado. Reintenta.',
  'error.other': 'La API respondió con un error ({status}).',
} as const;

export type TranslationKey = keyof typeof es;

const en: Record<TranslationKey, string> = {
  'app.subtitle': 'Answers with citations over an approved corpus. Hackathon demo: it does not measure onboarding time saved.',
  'app.healthWarning': 'The API is degraded or has no models configured: questions may fail.',
  'lang.label': 'Language',
  'sel.heading': 'Context',
  'sel.project': 'Project',
  'sel.role': 'Role',
  'sel.domain': 'Domain (team)',
  'sel.initiative': 'Initiative',
  'sel.general': 'General',
  'sel.all': 'All',
  'sel.overviewOnly': 'overview only',
  'route.heading': 'Onboarding route',
  'route.loading': 'Loading the route…',
  'route.ready': 'Ready',
  'route.pending': 'Pending',
  'route.pendingText': 'There is no approved documentation for this step yet.',
  'chat.heading': 'Ask about the project',
  'chat.empty': 'Pick a suggested question from the route or write your own.',
  'chat.searching': 'Searching the documentation…',
  'chat.retryIn': 'Retry in {n} s.',
  'chat.retry': 'Retry',
  'chat.placeholder': 'Write your question…',
  'chat.send': 'Send',
  'answer.supported': 'Supported by the documentation',
  'answer.partial': 'Partial: the documentation covers only part of it',
  'answer.insufficient': 'Insufficient evidence',
  'answer.helpfulQuestion': 'Was this helpful?',
  'answer.helpful': 'Helpful',
  'answer.notHelpful': 'Not helpful',
  'evidence.label': 'Evidence',
  'evidence.close': 'Close evidence',
  'evidence.section': 'section',
  'evidence.version': 'version',
  'evidence.excerpt': 'Cited excerpt',
  'evidence.openSource': 'Open the original source',
  'evidence.loading': 'Loading the document…',
  'evidence.fullDocument': 'Full document',
  'evidence.path': 'Path in the corpus',
  'evidence.versionLabel': 'Version',
  'evidence.sourceDate': 'Source date',
  'evidence.notRecorded': 'not recorded',
  'evidence.ingestedDate': 'Ingestion date',
  'evidence.snapshotNote':
    'This is a snapshot imported into the corpus. For htg-server documents the source date may be the import date, not the date of the last change in git.',
  'error.app': 'Something went wrong in the application.',
  'error.noConnection': 'There is no connection to the API. Check that it is running.',
  'error.badRequest': 'The request is not valid.',
  'error.unauthorized': 'The access token failed or expired. Restart scripts/start-web.ps1 to renew it.',
  'error.tooMany': 'Too many questions in a row. Wait a moment and retry.',
  'error.model': 'The model could not answer. Retry.',
  'error.timeout': 'The answer took too long. Retry.',
  'error.other': 'The API returned an error ({status}).',
};

export const TRANSLATIONS: Record<Language, Record<TranslationKey, string>> = { es, en };

export type Translate = (key: TranslationKey, params?: Record<string, string | number>) => string;

const STORAGE_KEY = 'onboarding.language';

function initialLanguage(): Language {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'es' || stored === 'en') {
      return stored;
    }
  } catch {
    // Storage can be blocked; fall through to the browser language.
  }

  return navigator.language?.toLowerCase().startsWith('es') ? 'es' : 'en';
}

/** The chosen language (Spanish or English) and the texts of the UI. It reads the signal, so templates update on change. */
@Injectable({ providedIn: 'root' })
export class I18n {
  readonly language = signal<Language>(initialLanguage());

  constructor() {
    document.documentElement.lang = this.language();
  }

  readonly t: Translate = (key, params) => {
    let text = TRANSLATIONS[this.language()][key];
    for (const [name, value] of Object.entries(params ?? {})) {
      text = text.replaceAll(`{${name}}`, String(value));
    }

    return text;
  };

  set(language: Language): void {
    this.language.set(language);
    document.documentElement.lang = language;
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // The choice still applies to this session.
    }
  }
}
