# CustomerClient

Angular-frontend för `..\TelemetryApi`, serverad av nginx – samma upplägg som
CQRSClient, men med **Grafana Faro** för telemetri från webbläsaren.

- `src/app/faro.ts` – `initializeFaro(...)` med webb-instrumentering och tracing
- `src/main.ts` – startar Faro före Angular
- `nginx.conf` – `/api/` → TelemetryApi, `/faro/` → faro-collectorn (samma origin, ingen CORS)
- `proxy.conf.json` – samma två proxies under `ng serve`

Faro lägger headern `traceparent` på HTTP-anropen. Därför blir ett klick i
webbläsaren, anropet i TelemetryApi och SQL-frågan **en och samma trace** i Tempo.

## Utveckling

```powershell
npm install
ng serve          # http://localhost:4200 - proxar till telemetryapi.local och faro.local
ng test
```

## Deploy

```powershell
./build.ps1       # docker build + push till localhost:5000, helm upgrade --install customer-client -n customers
```

Kräver `127.0.0.1 customerclient.local` i hosts-filen.
