import { splitCitations } from './citations';
import { ChatSource } from './models';

export type Inline =
  | { kind: 'text'; text: string; bold: boolean; code: boolean }
  | { kind: 'citation'; label: string; source: ChatSource };

export type AnswerBlock =
  | { kind: 'paragraph'; inlines: Inline[] }
  | { kind: 'list'; ordered: boolean; items: Inline[][] };

const LIST_ITEM = /^\s*(?:([-*•])|(\d+)[.)])\s+(.*)$/;
const EMPHASIS = /(\*\*[^*\n]+\*\*|`[^`\n]+`)/;

/**
 * Turns an answer into paragraphs and lists with bold and code, plus citation chips. It builds data, never HTML, so the
 * template renders it as text nodes. Anything it does not recognize (a lone "**", a "<b>") stays literal text.
 */
export function parseAnswer(answer: string, sources: readonly ChatSource[]): AnswerBlock[] {
  const blocks: AnswerBlock[] = [];
  let paragraph: string[] = [];
  let list: { ordered: boolean; items: string[] } | null = null;

  const flushParagraph = () => {
    if (paragraph.length > 0) {
      blocks.push({ kind: 'paragraph', inlines: inlines(paragraph.join('\n'), sources) });
      paragraph = [];
    }
  };
  const flushList = () => {
    if (list) {
      blocks.push({ kind: 'list', ordered: list.ordered, items: list.items.map((i) => inlines(i, sources)) });
      list = null;
    }
  };

  for (const line of answer.split(/\r?\n/)) {
    const item = LIST_ITEM.exec(line);
    if (item) {
      flushParagraph();
      const ordered = item[2] !== undefined;
      if (list && list.ordered !== ordered) {
        flushList();
      }
      list ??= { ordered, items: [] };
      list.items.push(item[3]);
    } else if (line.trim() === '') {
      flushParagraph();
      flushList();
    } else if (list && /^\s+\S/.test(line)) {
      // A wrapped continuation of the previous list item.
      list.items[list.items.length - 1] += ' ' + line.trim();
    } else {
      flushList();
      paragraph.push(line);
    }
  }

  flushParagraph();
  flushList();
  return blocks;
}

function inlines(text: string, sources: readonly ChatSource[]): Inline[] {
  const result: Inline[] = [];
  for (const part of text.split(EMPHASIS)) {
    if (part === '') {
      continue;
    }

    const bold = part.length > 4 && part.startsWith('**') && part.endsWith('**');
    const code = !bold && part.length > 2 && part.startsWith('`') && part.endsWith('`');
    const content = bold ? part.slice(2, -2) : code ? part.slice(1, -1) : part;

    for (const segment of splitCitations(content, sources)) {
      result.push(segment.kind === 'text'
        ? { kind: 'text', text: segment.text, bold, code }
        : { kind: 'citation', label: segment.label, source: segment.source });
    }
  }

  return result;
}
