# TelemetryApi

Ett enkelt CRUD-API för `Customer` (ingen CQRS) som visar **OpenTelemetry** i .NET:
traces till Tempo, metrics till Prometheus och loggar till Loki – via
otel-collectorn i namespace `monitoring`. Frontend: `..\CustomerClient`.

```
CustomerClient (nginx) ──/api──> TelemetryApi ──EF Core──> MySQL (databas customers)
       │                              │
       └──/faro──> faro-collector     └──OTLP──> otel-collector ──> Tempo / Prometheus / Loki
                         └────────────────────────────┘
```

## Förutsättningar

1. Klustret `k3d-home` och infrastrukturen: `..\Infra\deploy-infra.ps1`
2. Telemetristacken: `..\Telemetry\deploy-telemetry.ps1`
3. Hosts-filen (som administratör):

   ```
   127.0.0.1 telemetryapi.local customerclient.local
   127.0.0.1 grafana.local prometheus.local loki.local tempo.local otel.local otel-grpc.local faro.local
   ```

## Deploy

```powershell
cd D:\Kvällskurs\TelemetryApi
./setup-db.ps1      # en gång: databasen customers + användaren customers_app
./build.ps1         # dotnet publish -t:PublishContainer -> registry, helm upgrade --install

cd ..\CustomerClient
./build.ps1         # docker build/push + helm upgrade --install
```

Öppna http://customerclient.local och https://grafana.local (admin/admin).

## Var finns telemetrin i koden?

| Fil | Vad |
|---|---|
| `Telemetry/TelemetryExtensions.cs` | `AddOpenTelemetry()` – resource, tracing, metrics, logging, `UseOtlpExporter()` |
| `Telemetry/CustomerTelemetry.cs` | Egen `ActivitySource` och `Meter` (räknare för skapade/ändrade/raderade kunder) |
| `Customers/CustomerEndpoints.cs` | Strukturerade loggar, taggar på spans, eget span `customers.validate` |
| `Demo/DemoEndpoints.cs` | `/demo/slow` och `/demo/error` – något att titta på i Grafana |
| `charts/telemetry-api/templates/deployment.yaml` | `OTEL_SERVICE_NAME`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_RESOURCE_ATTRIBUTES` |

## Hitta det i Grafana

- **Tempo**: Explore → Tempo → Search → Service Name `telemetry-api` eller `customer-client`
- **Loki**: `{service_name="telemetry-api"}` – klicka på trace_id för att hoppa till Tempo
- **Prometheus**: `otel_customers_created_total`, `otel_http_server_request_duration_seconds_bucket`
- **Service Graph**: Explore → Tempo → Service Graph (byggs av Tempos metrics-generator)

## Lokalt (dotnet run)

`appsettings.Development.json` skickar telemetrin till `https://otel.local` (OTLP/HTTP)
och använder MySQL via `mysql.local:3306`. Kräver att mkcert-certifikatet från
`deploy-telemetry.ps1` är installerat.
