# Labbar kväll 8 - drift, hemligheter och helheten

Alla kommandon körs från den här mappen (`D:\Kvällskurs\Labbar\Kvall8`) i
PowerShell 7. Varje kommando står på en rad - inga backticks.

| Fil | Labb |
|---|---|
| `01-hpa-demo.yaml` | 2b: HorizontalPodAutoscaler i namespacet `lab` |
| `03-telemetry-api-values.yaml` | 3c: TelemetryApi läser connection string från SealedSecret:en `customers-db` |
| `04-telemetry-api-values.yaml` | 4c: som 03 + eget TLS-certifikat (`telemetry-api-tls`) för telemetryapi.local |
| `cert-info.ps1` | Visar namn, utfärdare och giltighet för en .crt-fil eller en server (ersätter openssl) |

## kubeseal

Kopiera `kubeseal.exe` från kväll 7 (eller hämta den från GitHub igen):

```powershell
Copy-Item ..\Kvall7\kubeseal.exe .
$ks = @("--controller-name", "sealed-secrets-controller", "--controller-namespace", "sealed-secrets")
```

`$ks` används med splatting (`.\kubeseal.exe @ks -o yaml`) i labb 3b och 4b.

## Det här skapas ikväll - och ska vara kvar

- `customers-db-sealed.yaml` - TelemetryApi:s connection string, krypterad
- `telemetry-api-tls-sealed.yaml` - certifikat och nyckel för telemetryapi.local, krypterade

Båda kan checkas in i Git. Radera `telemetryapi.key` efter labb 4b.
Kör alltid `TelemetryApi\build.ps1` med `-Values 04-telemetry-api-values.yaml`
från och med nu - annars återställs det gamla lösenordet och certifikatet.
