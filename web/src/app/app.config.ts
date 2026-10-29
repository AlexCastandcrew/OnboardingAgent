import { provideHttpClient } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { environment } from '../environments/environment';
import { HttpOnboardingApi } from './core/http-onboarding-api';
import { MockOnboardingApi } from './core/mock-onboarding-api';
import { ONBOARDING_API } from './core/onboarding-api';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideHttpClient(),
    { provide: ONBOARDING_API, useClass: environment.useMock ? MockOnboardingApi : HttpOnboardingApi },
  ],
};
