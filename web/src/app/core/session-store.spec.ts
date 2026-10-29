import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ChatRequest, ChatResponse } from './models';
import { MockOnboardingApi } from './mock-onboarding-api';
import { ONBOARDING_API } from './onboarding-api';
import { SessionStore } from './session-store';

describe('SessionStore', () => {
  let api: MockOnboardingApi;
  let store: SessionStore;
  const sent: ChatRequest[] = [];

  const answer = (text: string): ChatResponse => ({
    answerId: 'a1', answer: text, evidenceStatus: 'supported', sources: [], claims: [],
    diagnostics: { searchQuery: '', candidateCount: 0, contextCount: 0, reranked: false, generationAttempts: 1, elapsedMs: 0 },
  });

  beforeEach(async () => {
    sent.length = 0;
    api = new MockOnboardingApi();
    TestBed.configureTestingModule({ providers: [{ provide: ONBOARDING_API, useValue: api }] });
    store = TestBed.inject(SessionStore);
    await store.init();
  });

  it('retry_AfterModelFailure_DoesNotDuplicateTheQuestion', async () => {
    spyOn(api, 'chat').and.returnValues(
      throwError(() => new HttpErrorResponse({ status: 502 })),
      of(answer('Second try')),
    );

    await store.ask('What changes?');
    expect(store.chatError()?.retryable).toBeTrue();

    await store.retry();

    expect(store.chatError()).toBeNull();
    expect(store.messages().map((m) => m.kind)).toEqual(['user', 'assistant']);
  });

  it('ask_SecondQuestion_SendsThePreviousTurnsAsHistory', async () => {
    spyOn(api, 'chat').and.callFake((request) => {
      sent.push(request);
      return of(answer('First answer'));
    });

    await store.ask('First question');
    await store.ask('And the last one?');

    expect(sent[1].history).toEqual([
      { role: 'user', content: 'First question' },
      { role: 'assistant', content: 'First answer' },
    ]);
  });

  afterEach(() => localStorage.removeItem('onboarding.language'));

  it('setLanguage_ToEnglish_ReloadsTheRouteInEnglishKeepsTheConversationAndAsksForEnglish', async () => {
    const onboarding = spyOn(api, 'onboarding').and.callThrough();
    const chat = spyOn(api, 'chat').and.returnValue(of(answer('Hello')));
    await store.setLanguage('es');
    await store.ask('Una pregunta');

    await store.setLanguage('en');
    await store.ask('Another question');

    expect(onboarding.calls.mostRecent().args[2]).toBe('en');
    expect(store.messages().length).toBe(4);
    expect(chat.calls.mostRecent().args[0].language).toBe('en');
  });

  it('setLanguage_ApiFails_KeepsTheCurrentLanguageAndTheLoadedRoute', async () => {
    await store.setLanguage('es');
    const routeBefore = store.route();
    spyOn(api, 'onboarding').and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    await store.setLanguage('en');

    expect(store.route()).toBe(routeBefore);
    expect(store.routeError()).not.toBeNull();
    expect(localStorage.getItem('onboarding.language')).not.toBe('en');
  });

  it('initiatives_GeneralDomain_ListsEveryTeamsInitiatives', () => {
    expect(store.domain()).toBeNull();
    expect(store.initiatives().map((i) => i.domainId)).toContain('rhaegal');
    expect(store.initiatives().map((i) => i.domainId)).toContain('silverwing');
  });

  it('selectInitiative_UnderGeneral_SelectsTheTeamThatOwnsIt', async () => {
    await store.selectInitiative('H2G-6893');

    expect(store.domain()).toBe('silverwing');
    expect(store.initiative()).toBe('H2G-6893');
  });

  it('selectDomain_AfterPickingAnInitiative_ListsOnlyThatTeamsInitiatives', async () => {
    await store.selectDomain('rhaegal');

    expect(store.initiatives().every((i) => i.domainId === 'rhaegal')).toBeTrue();
  });

  it('selectRole_AfterAConversation_StartsANewConversation', async () => {
    spyOn(api, 'chat').and.returnValue(of(answer('Hello')));
    await store.ask('First question');

    await store.selectRole('qa');

    expect(store.messages()).toEqual([]);
  });
});
