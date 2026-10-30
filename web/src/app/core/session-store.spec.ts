import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, concat, of, throwError } from 'rxjs';
import { ChatActivity, ChatRequest, ChatResponse, ChatStreamEvent } from './models';
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

  const reply = (text: string) => of<ChatStreamEvent>({ type: 'response', response: answer(text) });
  const activity = (id: string, status: ChatActivity['status'], second: number): ChatStreamEvent => ({
    type: 'activity',
    activity: { id, step: id, status, message: id, timestamp: `2026-10-29T15:30:0${second}Z` },
  });

  it('ask_StreamWithActivities_KeepsThemWithTheAnswerAndClearsTheLiveFeed', async () => {
    spyOn(api, 'chatStream').and.returnValue(of(
      activity('search_documents', 'running', 1),
      activity('search_documents', 'completed', 3),
      { type: 'response', response: answer('Done') } as ChatStreamEvent,
    ));

    await store.ask('A question');

    const last = store.messages()[1];
    expect(last.kind === 'assistant' && last.activities.map((a) => a.status)).toEqual(['completed']);
    expect(store.activities()).toEqual([]);
  });

  it('ask_StreamFailsAfterSomeSteps_KeepsTheFeedWithTheUnfinishedStepFailedAndShowsTheError', async () => {
    spyOn(api, 'chatStream').and.returnValue(concat(of(activity('search_documents', 'running', 1)), throwError(() => new HttpErrorResponse({ status: 502 }))));

    await store.ask('A question');

    expect(store.activities().map((a) => a.status)).toEqual(['failed']);
    expect(store.chatError()?.retryable).toBeTrue();
    expect(store.asking()).toBeFalse();
  });

  it('newConversation_WhileAnswerInFlight_CancelsItAndIgnoresWhatArrivesLater', async () => {
    const stream = new Subject<ChatStreamEvent>();
    spyOn(api, 'chatStream').and.returnValue(stream);
    const pending = store.ask('A question');
    stream.next(activity('search_documents', 'running', 1));
    expect(store.activities().length).toBe(1);

    store.newConversation();
    stream.next({ type: 'response', response: answer('Too late') });
    await pending;

    expect(stream.observed).toBeFalse();
    expect(store.messages()).toEqual([]);
    expect(store.activities()).toEqual([]);
    expect(store.asking()).toBeFalse();
  });

  it('retry_AfterModelFailure_DoesNotDuplicateTheQuestion', async () => {
    spyOn(api, 'chatStream').and.returnValues(
      throwError(() => new HttpErrorResponse({ status: 502 })),
      reply('Second try'),
    );

    await store.ask('What changes?');
    expect(store.chatError()?.retryable).toBeTrue();

    await store.retry();

    expect(store.chatError()).toBeNull();
    expect(store.messages().map((m) => m.kind)).toEqual(['user', 'assistant']);
  });

  it('ask_SecondQuestion_SendsThePreviousTurnsAsHistory', async () => {
    spyOn(api, 'chatStream').and.callFake((request) => {
      sent.push(request);
      return reply('First answer');
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
    const chat = spyOn(api, 'chatStream').and.returnValue(reply('Hello'));
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

  it('newConversation_AfterQuestions_ClearsTheChatAndKeepsTheSelection', async () => {
    spyOn(api, 'chatStream').and.returnValue(reply('Hello'));
    await store.selectDomain('silverwing');
    await store.ask('A question');

    store.newConversation();

    expect(store.messages()).toEqual([]);
    expect(store.chatError()).toBeNull();
    expect(store.domain()).toBe('silverwing');
  });

  it('selectRole_AfterAConversation_StartsANewConversation', async () => {
    spyOn(api, 'chatStream').and.returnValue(reply('Hello'));
    await store.ask('First question');

    await store.selectRole('qa');

    expect(store.messages()).toEqual([]);
  });
});
