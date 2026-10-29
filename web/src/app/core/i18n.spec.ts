import { TRANSLATIONS } from './i18n';

describe('TRANSLATIONS', () => {
  it('TRANSLATIONS_SpanishAndEnglish_HaveTheSameKeys', () => {
    expect(Object.keys(TRANSLATIONS.en).sort()).toEqual(Object.keys(TRANSLATIONS.es).sort());
  });

  it('TRANSLATIONS_PlaceholdersInSpanish_ExistInEnglishToo', () => {
    const placeholders = (text: string) => (text.match(/\{\w+\}/g) ?? []).sort();

    for (const key of Object.keys(TRANSLATIONS.es) as (keyof typeof TRANSLATIONS.es)[]) {
      expect(placeholders(TRANSLATIONS.en[key])).withContext(key).toEqual(placeholders(TRANSLATIONS.es[key]));
    }
  });
});
