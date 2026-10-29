import { ChatHistoryItem, ChatResponse, MAX_HISTORY_ITEMS } from './models';

export type ChatMessage =
  | { kind: 'user'; text: string }
  | { kind: 'assistant'; response: ChatResponse };

/** The last turns the API accepts, with the assistant turn carried as the answer text. */
export function buildHistory(messages: readonly ChatMessage[]): ChatHistoryItem[] {
  return messages
    .map((m): ChatHistoryItem => (m.kind === 'user'
      ? { role: 'user', content: m.text }
      : { role: 'assistant', content: m.response.answer }))
    .slice(-MAX_HISTORY_ITEMS);
}
