import { splitCitations } from './citations';
import { ChatSource } from './models';

const source = (id: string): ChatSource => ({
  id, documentId: `doc-${id}`, chunkId: `doc-${id}#a`, title: `Title ${id}`, url: null, section: 'A',
  excerpt: 'x', sourceType: 'jira', sourceVersion: null, sourceUpdatedAt: null,
});

describe('splitCitations', () => {
  it('splitCitations_IdsInSources_ReturnsChipsBetweenText', () => {
    const segments = splitCitations('One [S1] two [S2].', [source('S1'), source('S2')]);

    expect(segments.map((s) => (s.kind === 'text' ? s.text : s.label))).toEqual(['One ', 'S1', ' two ', 'S2', '.']);
  });

  it('splitCitations_IdNotReturnedByTheApi_KeepsItAsPlainText', () => {
    const segments = splitCitations('Claim [S9] here.', [source('S1')]);

    expect(segments).toEqual([{ kind: 'text', text: 'Claim [S9] here.' }]);
  });

  it('splitCitations_MixOfKnownAndUnknownIds_OnlyKnownBecomeChips', () => {
    const segments = splitCitations('A [S1] B [S7] C', [source('S1')]);

    expect(segments.filter((s) => s.kind === 'citation').length).toBe(1);
    expect(segments.map((s) => (s.kind === 'text' ? s.text : s.label)).join('')).toBe('A S1 B [S7] C');
  });
});
