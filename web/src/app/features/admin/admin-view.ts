import { Component, computed, inject, signal } from '@angular/core';
import { ALL, filterPeople, rolesOf, summarize, teamsOf } from '../../core/admin';
import { I18n, TranslationKey } from '../../core/i18n';
import { Pace, PersonProgress, ROLES } from '../../core/models';
import { SessionStore } from '../../core/session-store';

/**
 * What an admin sees: everyone's level, steps, day and pace, filtered by team and role, with the averages of the people
 * shown. Question texts are never part of the data. Rows flagged `sample` are made-up people for the demo and say so.
 */
@Component({
  selector: 'app-admin-view',
  template: `
    <section class="admin" [attr.aria-label]="i18n.t('admin.title')">
      <header class="admin-head">
        <h1>{{ i18n.t('admin.title') }}</h1>
        <div class="admin-controls">
          <label>
            <span>{{ i18n.t('admin.team') }}</span>
            <select [value]="team()" (change)="onTeam($event)">
              <option [value]="all">{{ i18n.t('admin.all') }}</option>
              @for (id of teams(); track id) {
                <option [value]="id">{{ teamName(id) }}</option>
              }
            </select>
          </label>
          <label>
            <span>{{ i18n.t('admin.role') }}</span>
            <select [value]="role()" (change)="onRole($event)">
              <option [value]="all">{{ i18n.t('admin.all') }}</option>
              @for (id of roles(); track id) {
                <option [value]="id">{{ roleName(id) }}</option>
              }
            </select>
          </label>
          <button type="button" [disabled]="store.adminLoading()" (click)="store.loadAdmin()">{{ i18n.t('admin.refresh') }}</button>
          <button type="button" (click)="store.closeAdmin()">{{ i18n.t('admin.back') }}</button>
        </div>
      </header>

      @if (store.adminError(); as error) {
        <div class="chat-error" role="alert">
          <p>{{ error.title }}</p>
          @if (error.detail) {
            <p class="muted">{{ error.detail }}</p>
          }
        </div>
      } @else if (store.adminLoading() && store.adminOverview() === null) {
        <p class="muted">{{ i18n.t('admin.loading') }}</p>
      } @else {
        <dl class="admin-metrics">
          <div><dt>{{ i18n.t('admin.people') }}</dt><dd>{{ summary().people }}</dd></div>
          <div><dt>{{ i18n.t('admin.avgSteps') }}</dt><dd>{{ averageSteps() }}</dd></div>
          <div><dt>{{ i18n.t('admin.avgDay') }}</dt><dd>{{ averageDay() }}</dd></div>
          <div><dt>{{ i18n.t('admin.behind') }}</dt><dd class="admin-behind">{{ summary().behind }}</dd></div>
        </dl>

        @if (filtered().length === 0) {
          <p class="muted">{{ i18n.t('admin.empty') }}</p>
        } @else {
          <div class="admin-table-wrap">
            <table class="admin-table">
              <thead>
                <tr>
                  <th scope="col">{{ i18n.t('admin.col.person') }}</th>
                  <th scope="col">{{ i18n.t('admin.col.team') }}</th>
                  <th scope="col">{{ i18n.t('admin.col.role') }}</th>
                  <th scope="col">{{ i18n.t('admin.col.level') }}</th>
                  <th scope="col">{{ i18n.t('admin.col.steps') }}</th>
                  <th scope="col">{{ i18n.t('admin.col.day') }}</th>
                  <th scope="col">{{ i18n.t('admin.col.pace') }}</th>
                  <th scope="col">{{ i18n.t('admin.col.activity') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (p of filtered(); track p.person) {
                  <tr>
                    <td>
                      {{ p.person }}
                      @if (p.sample) {
                        <span class="sample-tag">{{ i18n.t('admin.sample') }}</span>
                      }
                    </td>
                    <td>{{ teamName(p.team) }}</td>
                    <td>{{ roleName(p.role) }}</td>
                    <td>{{ p.level }} / 4 · {{ i18n.t(levelKey(p.level)) }}</td>
                    <td>
                      <span class="step-bars" role="img" [attr.aria-label]="p.stepsDone + ' / ' + p.stepsTotal">
                        @for (n of bars(p); track $index) {
                          <i [class.done]="n"></i>
                        }
                      </span>
                    </td>
                    <td>{{ p.day }}</td>
                    <td><span class="pace-chip" [attr.data-pace]="p.pace">{{ i18n.t(paceKey(p.pace)) }}</span></td>
                    <td class="muted">{{ lastActivity(p) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <p class="muted admin-note">{{ i18n.t('admin.target', { n: store.adminOverview()?.targetDays ?? 14 }) }}</p>
        @if (hasSample()) {
          <p class="muted admin-note">{{ i18n.t('admin.sampleNote') }}</p>
        }
        <p class="muted admin-note">{{ i18n.t('admin.privacy') }}</p>
      }
    </section>
  `,
})
export class AdminViewComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly all = ALL;

  protected readonly team = signal(ALL);
  protected readonly role = signal(ALL);

  private readonly people = computed(() => this.store.adminOverview()?.people ?? []);
  protected readonly teams = computed(() => teamsOf(this.people()));
  protected readonly roles = computed(() => rolesOf(this.people()));
  protected readonly filtered = computed(() => filterPeople(this.people(), { team: this.team(), role: this.role() }));
  protected readonly summary = computed(() => summarize(this.filtered()));
  protected readonly hasSample = computed(() => this.people().some((p) => p.sample));
  protected readonly averageSteps = computed(() => {
    const { averageSteps, averageTotal } = this.summary();
    return averageSteps === null || averageTotal === null ? '-' : `${averageSteps.toFixed(1)} / ${averageTotal.toFixed(0)}`;
  });
  protected readonly averageDay = computed(() => {
    const { averageDay } = this.summary();
    return averageDay === null ? '-' : String(Math.round(averageDay));
  });

  protected onTeam(event: Event): void {
    this.team.set((event.target as HTMLSelectElement).value);
  }

  protected onRole(event: Event): void {
    this.role.set((event.target as HTMLSelectElement).value);
  }

  protected teamName(id: string): string {
    if (id === 'general') {
      return this.i18n.t('admin.general');
    }

    return this.store.domains().find((d) => d.domainId === id)?.name ?? id.charAt(0).toUpperCase() + id.slice(1);
  }

  protected roleName(id: string): string {
    return ROLES.find((r) => r.id === id)?.label ?? id;
  }

  protected levelKey(level: number): TranslationKey {
    return `me.level.${Math.min(Math.max(level, 1), 4)}` as TranslationKey;
  }

  protected paceKey(pace: Pace): TranslationKey {
    return `pace.${pace}` as TranslationKey;
  }

  protected bars(person: PersonProgress): boolean[] {
    return Array.from({ length: Math.max(person.stepsTotal, 0) }, (_, i) => i < person.stepsDone);
  }

  protected lastActivity(person: PersonProgress): string {
    return new Date(person.lastActivityAt).toLocaleString(this.i18n.language(), { dateStyle: 'short', timeStyle: 'short' });
  }
}
