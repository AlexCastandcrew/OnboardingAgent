import { ChatSource } from './models';

export type AnswerSegment =
  | { kind: 'text'; text: string }
  | { kind: 'citation'; label: string; source: ChatSource };

const CITATION = /\[(S\d+)\]/g;

/**
 * Splits an answer into text and citation chips. Only ids present in sources become chips; any other [S#]
 * stays as plain text, so an id the backend did not return is never shown as evidence.
 */
export function splitCitations(answer: string, sources: readonly ChatSource[]): AnswerSegment[] {
  const byId = new Map(sources.map((s) => [s.id, s]));
  const segments: AnswerSegment[] = [];
  let last = 0;

  for (const match of answer.matchAll(CITATION)) {
    const source = byId.get(match[1]);
    if (!source) {
      continue;
    }

    if (match.index > last) {
      segments.push({ kind: 'text', text: answer.slice(last, match.index) });
    }
    segments.push({ kind: 'citation', label: match[1], source });
    last = match.index + match[0].length;
  }

  if (last < answer.length) {
    segments.push({ kind: 'text', text: answer.slice(last) });
  }

  return segments;
}
