// Mirrors src/OnboardingAgent.Core/Contracts/Contracts.cs (JSON is camelCase, enums are camelCase strings).

export type Role = 'developer' | 'qa' | 'leader';

export type EvidenceStatus = 'supported' | 'partial' | 'insufficient';

export interface ChatHistoryItem {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  projectId: string;
  domain?: string;
  initiative?: string;
  role: Role;
  /** Language of the answer: "es" or "en". */
  language?: 'es' | 'en';
  message: string;
  history?: ChatHistoryItem[];
}

export interface ChatSource {
  id: string;
  documentId: string;
  chunkId: string;
  title: string;
  url: string | null;
  section: string;
  excerpt: string;
  sourceType: string;
  sourceVersion: string | null;
  sourceUpdatedAt: string | null;
}

export interface ChatClaim {
  text: string;
  sourceIds: string[];
}

export interface ChatDiagnostics {
  searchQuery: string;
  candidateCount: number;
  contextCount: number;
  reranked: boolean;
  generationAttempts: number;
  elapsedMs: number;
}

export interface ChatResponse {
  answerId: string;
  answer: string;
  evidenceStatus: EvidenceStatus;
  sources: ChatSource[];
  claims: ChatClaim[];
  diagnostics: ChatDiagnostics;
}

/** One step of the agent while it answers (mirrors ChatActivity in Contracts.cs). The same id arrives again as its status changes. */
export type ActivityStatus = 'pending' | 'running' | 'completed' | 'failed';

export interface SourceTypeCount {
  sourceType: string;
  documents: number;
  passages: number;
}

export interface ActivityDetail {
  candidates?: number;
  kept?: number;
  attempt?: number;
  cited?: number;
  fallback?: boolean;
  sources?: SourceTypeCount[];
}

export interface ChatActivity {
  id: string;
  step: string;
  status: ActivityStatus;
  /** English text from the server; the UI localizes from `step` and `status`. */
  message: string;
  timestamp: string;
  detail?: ActivityDetail;
}

/** What the chat stream delivers: any number of activities, then the answer. A failure ends the stream as an error. */
export type ChatStreamEvent =
  | { type: 'activity'; activity: ChatActivity }
  | { type: 'response'; response: ChatResponse };

export interface ProjectSummary {
  projectId: string;
  name: string;
  description: string;
  repoUrl: string | null;
  snapshotDate: string;
}

export interface InitiativeSummary {
  initiativeId: string;
  name: string;
}

/** What the index holds for a project (mirrors KnowledgeSummary in Contracts.cs). */
export interface SourceTypeSummary {
  sourceType: string;
  documents: number;
  passages: number;
}

export interface RepositorySummary {
  name: string;
  url: string;
  documents: number;
  version: string | null;
}

export interface KnowledgeSummary {
  projectId: string;
  documents: number;
  passages: number;
  lastIndexedAt: string | null;
  sourceTypes: SourceTypeSummary[];
  repositories: RepositorySummary[];
  teams: number;
  initiatives: number;
}

export interface DomainSummary {
  domainId: string;
  name: string;
  /** "full" when the corpus covers the team in depth, "overview" when only the team overview exists. */
  depth: 'full' | 'overview';
  initiatives: InitiativeSummary[];
}

export interface OnboardingDocument {
  documentId: string;
  title: string;
  sourceType: string;
  url: string | null;
}

export interface OnboardingLink {
  label: string;
  url: string;
}

export interface OnboardingStep {
  step: number;
  key: string;
  title: string;
  goal: string;
  /** "ready" when at least one corpus document backs the step, "pending" otherwise. */
  status: 'ready' | 'pending';
  documentIds: string[];
  documents: OnboardingDocument[];
  links: OnboardingLink[];
  suggestedQuestions: string[];
}

export interface OnboardingRoute {
  projectId: string;
  role: Role;
  domain: string | null;
  initiative: string | null;
  steps: OnboardingStep[];
}

export interface DocumentSection {
  chunkId: string;
  section: string;
}

export interface DocumentView {
  documentId: string;
  projectId: string;
  title: string;
  sourceType: string;
  url: string | null;
  path: string;
  sourceVersion: string | null;
  sourceUpdatedAt: string | null;
  ingestedAt: string;
  domains: string[];
  initiatives: string[];
  content: string;
  sections: DocumentSection[];
}

export interface FeedbackRequest {
  answerId: string;
  helpful: boolean;
  comment?: string;
}

export interface HealthResponse {
  status: 'ok' | 'degraded';
  index: { documents: number; chunks: number; lastIngestedAt: string | null };
  models: { region: string; answerModelId: string; auxModelId: string; auth: string; configured: boolean };
}

export const ROLES: readonly { id: Role; label: string }[] = [
  { id: 'developer', label: 'Developer' },
  { id: 'qa', label: 'QA' },
  { id: 'leader', label: 'Leader' },
];

export const MAX_MESSAGE_CHARS = 2000;
export const MAX_HISTORY_ITEMS = 6;
