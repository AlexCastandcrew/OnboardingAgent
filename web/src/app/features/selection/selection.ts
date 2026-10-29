import { Component, inject } from '@angular/core';
import { I18n } from '../../core/i18n';
import { ROLES, Role } from '../../core/models';
import { SessionStore } from '../../core/session-store';

@Component({
  selector: 'app-selection',
  template: `
    <section class="panel menu" [attr.aria-label]="i18n.t('sel.heading')">
      <div class="panel-heading"><h2>{{ i18n.t('sel.heading') }}</h2></div>
      <div class="panel-body">
      <label>
        {{ i18n.t('sel.project') }}
        <select [value]="store.projectId() ?? ''" (change)="store.selectProject(value($event))">
          @for (p of store.projects(); track p.projectId) {
            <option [value]="p.projectId">{{ p.name }}</option>
          }
        </select>
      </label>

      <label>
        {{ i18n.t('sel.role') }}
        <select [value]="store.role()" (change)="store.selectRole(role($event))">
          @for (r of roles; track r.id) {
            <option [value]="r.id">{{ r.label }}</option>
          }
        </select>
      </label>

      <label>
        {{ i18n.t('sel.domain') }}
        <select [value]="store.domain() ?? ''" (change)="store.selectDomain(value($event) || null)">
          <option value="">{{ i18n.t('sel.general') }}</option>
          @for (d of store.domains(); track d.domainId) {
            <option [value]="d.domainId">{{ d.name }}{{ d.depth === 'overview' ? ' (' + i18n.t('sel.overviewOnly') + ')' : '' }}</option>
          }
        </select>
      </label>

      <label>
        {{ i18n.t('sel.initiative') }}
        <select
          [value]="store.initiative() ?? ''"
          [disabled]="store.initiatives().length === 0"
          (change)="store.selectInitiative(value($event) || null)">
          <option value="">{{ i18n.t('sel.all') }}</option>
          @for (i of store.initiatives(); track i.initiativeId) {
            <option [value]="i.initiativeId">{{ i.initiativeId }} · {{ i.name }}{{ store.domain() ? '' : ' (' + i.domainName + ')' }}</option>
          }
        </select>
      </label>
      </div>
    </section>
  `,
})
export class SelectionComponent {
  protected readonly store = inject(SessionStore);
  protected readonly i18n = inject(I18n);
  protected readonly roles = ROLES;

  protected value(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }

  protected role(event: Event): Role {
    return this.value(event) as Role;
  }
}
