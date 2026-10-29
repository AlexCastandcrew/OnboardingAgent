import { AnswerBlock, Inline, parseAnswer } from './markdown';
import { ChatSource } from './models';

const source = (id: string): ChatSource => ({
  id, documentId: `doc-${id}`, chunkId: `doc-${id}#a`, title: `Title ${id}`, url: null, section: 'A',
  excerpt: 'x', sourceType: 'jira', sourceVersion: null, sourceUpdatedAt: null,
});

const flat = (inlines: Inline[]) =>
  inlines.map((i) => (i.kind === 'citation' ? `[${i.label}]` : i.bold ? `<b>${i.text}</b>` : i.code ? `<c>${i.text}</c>` : i.text)).join('');

const list = (block: AnswerBlock) => (block.kind === 'list' ? block : null);

describe('parseAnswer', () => {
  it('parseAnswer_BoldInsideAListItem_KeepsBoldAndCitationOrder', () => {
    const blocks = parseAnswer('Intro [S1]\n\n- **Rhaegal**: YEP [S2]\n- **Vhagar**: Splits', [source('S1'), source('S2')]);

    expect(blocks.length).toBe(2);
    expect(flat((blocks[0] as { inlines: Inline[] }).inlines)).toBe('Intro [S1]');
    expect(list(blocks[1])?.items.map(flat)).toEqual(['<b>Rhaegal</b>: YEP [S2]', '<b>Vhagar</b>: Splits']);
  });

  it('parseAnswer_ListRightAfterParagraphWithoutBlankLine_StartsANewBlock', () => {
    const blocks = parseAnswer('Teams:\n- One\n- Two', []);

    expect(blocks.map((b) => b.kind)).toEqual(['paragraph', 'list']);
  });

  it('parseAnswer_NumberedItems_AreAnOrderedList', () => {
    const blocks = parseAnswer('1. First\n2. Second', []);

    expect(list(blocks[0])?.ordered).toBeTrue();
  });

  it('parseAnswer_UnmatchedMarkersAndHtml_StayLiteralText', () => {
    const blocks = parseAnswer('A lone ** star and <b>tag</b>', []);

    expect(flat((blocks[0] as { inlines: Inline[] }).inlines)).toBe('A lone ** star and <b>tag</b>');
  });

  it('parseAnswer_CitationNotReturnedByTheApi_IsNotAChip', () => {
    const blocks = parseAnswer('Claim [S9].', [source('S1')]);

    expect((blocks[0] as { inlines: Inline[] }).inlines.some((i) => i.kind === 'citation')).toBeFalse();
  });
});
