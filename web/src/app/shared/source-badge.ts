import { Component, computed, inject, input } from '@angular/core';
import { I18n, TranslationKey } from '../core/i18n';
import { IconComponent, IconName } from './icon';

const KNOWN: Record<string, { icon: IconName; label: TranslationKey }> = {
  confluence: { icon: 'book-open', label: 'src.confluence' },
  jira: { icon: 'list-checks', label: 'src.jira' },
  repo: { icon: 'git-branch', label: 'src.repo' },
  manual: { icon: 'file', label: 'src.manual' },
  synthetic: { icon: 'flask', label: 'src.synthetic' },
};

/**
 * Where information comes from (Confluence, Jira, a repository, a manual document...) as a solid, vivid chip, one color per
 * kind, so a connection to a documentation source stands out from the rest of the text. An unknown type is shown neutral.
 */
@Component({
  selector: 'app-source-badge',
  imports: [IconComponent],
  template: `<span class="src-badge" [class]="'src-' + kind()" [attr.title]="label()"><app-icon [name]="icon()" />{{ label() }}@if (count() !== null) {<b>{{ count() }}</b>}</span>`,
})
export class SourceBadgeComponent {
  private readonly i18n = inject(I18n);

  readonly type = input.required<string>();
  /** Optional number shown after the name (for example the passages taken from that kind of source). */
  readonly count = input<number | null>(null);

  protected readonly kind = computed(() => (this.type() in KNOWN ? this.type() : 'other'));
  protected readonly icon = computed<IconName>(() => KNOWN[this.type()]?.icon ?? 'file');
  protected readonly label = computed(() => (this.type() in KNOWN ? this.i18n.t(KNOWN[this.type()].label) : this.type()));
}
