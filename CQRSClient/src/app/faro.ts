import { faro, getWebInstrumentations, initializeFaro } from '@grafana/faro-web-sdk';
import { TracingInstrumentation } from '@grafana/faro-web-tracing';

/**
 * Grafana Faro - telemetri från webbläsaren.
 *
 * Skickar till /faro/collect på SAMMA adress som appen. nginx (och
 * proxy.conf.json under ng serve) skickar vidare till faro-collectorn i
 * namespace monitoring - samma knep som /api, så ingen CORS behövs.
 *
 *   getWebInstrumentations()   - fel, console-loggar, Web Vitals, sidvisningar
 *   TracingInstrumentation     - ett span per HTTP-anrop, och headern
 *                                traceparent på anropet. Därför fortsätter
 *                                samma trace in i CQRSApi - och via NATS vidare till workern.
 */
export function initTelemetry(): void {
  initializeFaro({
    url: `${window.location.origin}/faro/collect`,
    app: {
      // Blir service.name i Tempo och Loki.
      name: 'cqrs-client',
      version: '1.0.0',
      environment: 'k3d',
    },
    instrumentations: [...getWebInstrumentations(), new TracingInstrumentation()],
  });
}

/** Egen händelse - syns i Loki med kind=event. */
export function trackEvent(name: string, attributes: Record<string, string> = {}): void {
  faro.api?.pushEvent(name, attributes);
}
