import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'onboarding.demo-session';

/**
 * Demo only. There is no login service: this check runs in the browser and the credentials are in the bundle, so it
 * protects nothing. The API keeps its own bearer token (added by the dev proxy). Replace with real sign-in before any
 * real use.
 */
const DEMO_USER = { name: 'Frank Rivera', email: 'frank.rivera@castandcrew.com', initials: 'FR', company: 'Cast & Crew' };
const DEMO_PASSWORD = 'clave12345';

@Injectable({ providedIn: 'root' })
export class DemoAuth {
  readonly user = DEMO_USER;
  readonly signedIn = signal(sessionStorage.getItem(STORAGE_KEY) === '1');

  /** True when the credentials match; the session lasts until the tab closes. */
  signIn(email: string, password: string): boolean {
    if (email.trim().toLowerCase() !== DEMO_USER.email || password !== DEMO_PASSWORD) {
      return false;
    }

    sessionStorage.setItem(STORAGE_KEY, '1');
    this.signedIn.set(true);
    return true;
  }

  signOut(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.signedIn.set(false);
  }
}
