import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { initTelemetry } from './app/faro';

// Starta Faro FÖRST, så att även fel under uppstarten fångas.
initTelemetry();

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
