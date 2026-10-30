import { Component, input } from '@angular/core';

export type IconName =
  | 'layers' | 'code' | 'users' | 'compass' | 'file' | 'link' | 'check' | 'menu' | 'x' | 'sparkles' | 'arrow-up' | 'plus' | 'chevron-down'
  | 'alert' | 'git-branch' | 'book-open' | 'list-checks' | 'flask';

/** Lucide-style line icons (24x24, 2px round stroke), drawn inline so no icon package is needed. */
@Component({
  selector: 'app-icon',
  template: `
    <svg class="icon" viewBox="0 0 24 24" aria-hidden="true">
      @switch (name()) {
        @case ('layers') { <path d="m12 2 10 5-10 5L2 7z" /><path d="m2 17 10 5 10-5" /><path d="m2 12 10 5 10-5" /> }
        @case ('code') { <path d="m16 18 6-6-6-6" /><path d="m8 6-6 6 6 6" /> }
        @case ('users') {
          <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" />
          <path d="M22 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" />
        }
        @case ('compass') { <circle cx="12" cy="12" r="10" /><path d="m16.24 7.76-2.12 6.36-6.36 2.12 2.12-6.36z" /> }
        @case ('file') {
          <path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7z" /><path d="M14 2v4a2 2 0 0 0 2 2h4" /><path d="M10 13H8" /><path d="M16 17H8" />
        }
        @case ('link') {
          <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" /><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />
        }
        @case ('check') { <path d="M20 6 9 17l-5-5" /> }
        @case ('menu') { <path d="M4 6h16" /><path d="M4 12h16" /><path d="M4 18h16" /> }
        @case ('x') { <path d="M18 6 6 18" /><path d="m6 6 12 12" /> }
        @case ('sparkles') { <path d="m12 3 1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z" /><path d="M19 3v4" /><path d="M21 5h-4" /> }
        @case ('arrow-up') { <path d="M12 19V5" /><path d="m5 12 7-7 7 7" /> }
        @case ('plus') { <path d="M12 5v14" /><path d="M5 12h14" /> }
        @case ('chevron-down') { <path d="m6 9 6 6 6-6" /> }
        @case ('alert') { <circle cx="12" cy="12" r="10" /><path d="M12 8v4" /><path d="M12 16h.01" /> }
        @case ('git-branch') { <path d="M6 3v12" /><circle cx="18" cy="6" r="3" /><circle cx="6" cy="18" r="3" /><path d="M18 9a9 9 0 0 1-9 9" /> }
        @case ('book-open') { <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z" /><path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z" /> }
        @case ('list-checks') { <path d="m3 17 2 2 4-4" /><path d="m3 7 2 2 4-4" /><path d="M13 6h8" /><path d="M13 12h8" /><path d="M13 18h8" /> }
        @case ('flask') { <path d="M10 2v7.5L4.7 20.5A1 1 0 0 0 5.6 22h12.8a1 1 0 0 0 .9-1.5L14 9.5V2" /><path d="M8.5 2h7" /><path d="M7 16h10" /> }
      }
    </svg>
  `,
})
export class IconComponent {
  readonly name = input.required<IconName>();
}
