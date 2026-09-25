# CQRS i klustret – CQRSApi + CQRSWorker

CQRS står för *Command Query Responsibility Segregation*: **läsning** (query) och **skrivning** (command) går olika vägar. Här är de dessutom två helt separata program i klustret.

```
                       Traefik-ingress (cqrsapi.local)
                                   │
                              ┌────▼─────┐
             GET /persons ───►│ CQRSApi  │◄─── POST/PUT/DELETE /persons
                              └──┬────┬──┘
                   Query (Dapper)│    │Command (publicera, svara 202 Accepted)
                                 │    ▼
                                 │  ┌───────────────────────────┐
                                 │  │ NATS JetStream            │
                                 │  │ ström PERSONS             │
                                 │  │ persons.cmd.create/update/│
                                 │  │ delete                    │
                                 │  └────────────┬──────────────┘
                                 │               │ durable consumer "cqrs-worker"
                                 │          ┌────▼───────┐
                                 │          │ CQRSWorker │  (ingen Service, ingen Ingress)
                                 │          └────┬───────┘
                                 │               │ EF Core (äger schemat, enda som skriver)
                              ┌──▼───────────────▼──┐
                              │ MySQL  databas cqrs │
                              │ tabell Persons      │
                              └─────────────────────┘
          cqrs_reader (bara SELECT)            cqrs_writer (alla rättigheter)
```

| | CQRSApi | CQRSWorker |
|---|---|---|
| Roll | Utåtriktad: tar emot HTTP | Utför kommandon |
| Läser | Dapper + ren SQL | – |
| Skriver | **Aldrig** till databasen – publicerar kommandon på JetStream | EF Core |
| Databasanvändare | `cqrs_reader` (SELECT) | `cqrs_writer` (äger schemat) |
| Nås via | Service + Ingress | Bara via NATS |

## Det här visar mönstret

- **202 Accepted istället för 201 Created.** API:t säger "jag har tagit emot kommandot", inte "det är klart". Svaret innehåller `personId` och en `Location`-header. Klienten frågar sedan med GET. Mellan de två anropen finns *eventual consistency*: en GET direkt efter POST kan ge 404 i några millisekunder.
- **API:t bestämmer id:t.** Då vet klienten direkt vart den ska fråga, trots att ingenting är sparat än.
- **JetStream är en kö som tål krascher.** Strömmen `PERSONS` har WorkQueue-retention: ett kommando ligger kvar tills workern kvitterat (ack) det. Stoppa workern (`kubectl scale deploy/cqrs-worker --replicas=0 -n cqrs`) och skicka några POST. Starta den igen, så betas allt av.
- **Minst en gång ⇒ idempotenta hanterare.** Varje kommando har ett `CommandId` som skickas som `Nats-Msg-Id` (JetStream slänger dubbletter). Workern ignorerar dessutom en Create för ett id som redan finns.
- **Oberoende kontrakt.** `Contracts/PersonCommand.cs` finns i *båda* projekten med avsikt. Kontraktet är JSON:en på subjektet, inte en delad DLL.
- **Rättigheter speglar mönstret.** API:ts databasanvändare kan inte skriva, även om någon skulle försöka.

## Struktur

```
CQRS/
├── CQRSApi/                 Minimal API: /persons (GET, POST, PUT, DELETE), /healthz, /scalar
│   ├── Contracts/           PersonCommand + subjekt
│   ├── Messaging/           Publicering på JetStream + skapar strömmen
│   └── Queries/             Dapper-frågor + läsmodellen PersonDto
├── CQRSWorker/              BackgroundService som konsumerar JetStream
│   ├── Contracts/           Samma kontrakt som API:t
│   ├── Data/                Person + PersonsDbContext (EF Core, MySQL)
│   └── Worker.cs            Konsument + idempotenta Create/Update/Delete
├── charts/
│   ├── cqrs-api/            Deployment, Service, Ingress, Secret
│   └── cqrs-worker/         Deployment, Secret
├── setup-db.ps1             Skapar databasen cqrs + cqrs_writer/cqrs_reader i befintlig MySQL
└── build.ps1                Bygg, pusha och helm upgrade --install båda
```

## Kom igång

Förutsättning: klustret `home` från kväll 2 (k3d-install.yaml) är igång.

**1. Infrastruktur (en gång)** – MySQL och NATS kommer från kursens gemensamma `deploy-infra.ps1`:

| Tjänst | I klustret | Från Windows |
|---|---|---|
| MySQL (bitnami, release `mysql`) | `mysql.mysql.svc.cluster.local:3306` | `localhost:3306` |
| NATS med JetStream (release `nats`) | `nats://nats.nats.svc.cluster.local:4222` | `nats://localhost:4222` |

Kör sedan `setup-db.ps1` en gång. Det skapar databasen `cqrs` och de två användarna i den befintliga MySQL-instansen:

```powershell
cd D:\Kvällskurs\CQRS
./setup-db.ps1        # root-lösenordet är samma default som $MySQLRootPwd i deploy-infra.ps1
```

**2. Hosts-filen:** lägg till `127.0.0.1  cqrsapi.local` (öppna Notepad som administratör).

**3. Bygg och deploya**

```powershell
./build.ps1
kubectl get pods -n cqrs
kubectl logs -f deploy/cqrs-worker -n cqrs     # se kommandona utföras
```

**4. Testa** med `CQRSApi/CQRSApi.http` i Visual Studio eller på http://cqrsapi.local/scalar.

### Köra lokalt från Visual Studio

Portarna 3306 och 4222 är forwardade från klustret till din dator, och `appsettings.json` pekar på `mysql.local` och `nats.local` (kräver raderna i hosts-filen) och båda projekten kan köras med F5 mot MySQL och NATS i klustret. Stoppa `cqrs-worker` i klustret först (`--replicas=0`), annars delar de två workerna på kön.

## Helm-values

Allt kan ändras med `--set` eller en egen values-fil (`./build.ps1 -ApiValues min-fil.yaml`).

**cqrs-api**

| Värde | Default | Beskrivning |
|---|---|---|
| `database.connectionString` | `...User ID=cqrs_reader...` | Läs-sidans connection string (hamnar i en Secret) |
| `database.existingSecret` / `existingSecretKey` | `""` / `connectionString` | Använd en befintlig Secret istället |
| `nats.url` | `nats://nats.nats.svc.cluster.local:4222` | NATS-servern |
| `nats.stream` | `PERSONS` | JetStream-strömmen (samma som workern) |
| `cors.mode` | `AllowAll` | `AllowAll`, `AllowOrigins` eller `Disabled` |
| `cors.origins` | `[]` | Tillåtna origins när mode är `AllowOrigins` |
| `ingress.enabled` / `host` / `className` | `true` / `cqrsapi.local` / `traefik` | Ingressen |
| `openApi.enabled` | `true` | `/openapi/v1.json` och `/scalar` |
| `replicaCount` | `2` | API:t är tillståndslöst och kan skalas fritt |
| `extraEnv` | `{}` | Extra miljövariabler |

**cqrs-worker**

| Värde | Default | Beskrivning |
|---|---|---|
| `database.connectionString` | `...User ID=cqrs_writer...` | Skriv-sidans connection string (Secret) |
| `nats.url` / `nats.stream` | som ovan | |
| `nats.consumer` | `cqrs-worker` | Den varaktiga konsumentens namn |
| `replicaCount` | `1` | Håll 1 för att garantera ordningen på kommandona |

Exempel med CORS för en Angular-frontend:

```powershell
helm upgrade cqrs-api ./charts/cqrs-api -n cqrs --reuse-values `
  --set cors.mode=AllowOrigins `
  --set "cors.origins={http://angular.local,http://localhost:4200}"
```

Varje värde blir en miljövariabel i podden (`Cors__Mode`, `Cors__Origins__0`, `Nats__Url`, `ConnectionStrings__ReadDb` …). ASP.NET Core läser dem som konfiguration och de överstyr `appsettings.json`.

## Felsökning

| Symptom | Troligen |
|---|---|
| GET ger 500 `Table 'cqrs.Persons' doesn't exist` | Workern har inte startat än. Det är den som skapar tabellen. |
| POST ger 500 `no responders` / stream not found | NATS saknas eller JetStream är inte påslaget (`config.jetstream.enabled`) |
| Workern loggar `Kunde inte skapa databasschemat` | MySQL är inte redo än. Workern försöker igen var 5:e sekund. |
| Workern eller API:t loggar `Access denied` | `setup-db.ps1` har inte körts, eller lösenorden i values stämmer inte med skriptets. Kör skriptet igen (det är idempotent). |
| POST ger 202 men personen dyker aldrig upp | `kubectl logs deploy/cqrs-worker -n cqrs`. Kolla också att `nats.stream` är samma i båda charts. |
