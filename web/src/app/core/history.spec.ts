import { ChatMessage, buildHistory } from './history';
import { ChatResponse } from './models';

const reply = (answer: string): ChatMessage => ({
  kind: 'assistant',
  response: { answer } as ChatResponse,
});

describe('buildHistory', () => {
  it('buildHistory_AssistantTurn_CarriesTheAnswerText', () => {
    const history = buildHistory([{ kind: 'user', text: 'Q1' }, reply('A1 [S1]')]);

    expect(history).toEqual([
      { role: 'user', content: 'Q1' },
      { role: 'assistant', content: 'A1 [S1]' },
    ]);
  });

  it('buildHistory_MoreThanSixTurns_KeepsTheLastSix', () => {
    const messages: ChatMessage[] = Array.from({ length: 8 }, (_, i): ChatMessage => ({ kind: 'user', text: `Q${i}` }));

    const history = buildHistory(messages);

    expect(history.map((h) => h.content)).toEqual(['Q2', 'Q3', 'Q4', 'Q5', 'Q6', 'Q7']);
  });
});
