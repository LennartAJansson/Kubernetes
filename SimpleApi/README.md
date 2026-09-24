# SimpleApi – bygg & deploy till Kubernetes (ubk3s)

Minimal ASP.NET Core Web API (mall-endpointen `/weatherforecast`) som byggs som
container-image med .NET SDK:ns inbyggda containerisering (ingen Dockerfile)
och deployas till ett lokalt k3d-kluster (`ubk3s-home`) via `build.ps1`.

## Innehåll

```
SimpleApi/
├── build.ps1                 # Bygger, versionssätter, pushar och deployar
├── k8s/
│   ├── namespace.yaml         # Namespace "simpleapi"
│   ├── deployment.yaml        # Deployment (pod-mall, probes, resources)
│   ├── service.yaml           # ClusterIP-service framför poddarna
│   └── ingress.yaml           # Ingress: simpleapi.ubk3s → Service
└── SimpleApi/
    ├── SimpleApi.csproj       # ContainerBaseImage, ContainerPort m.m.
    └── Program.cs
```

## Förutsättningar

Innan `.\build.ps1` körs behöver följande vara på plats:

- **kubectl-context.** Ett context som heter `ubk3s-home` måste finnas i din
  kubeconfig (`kubectl config get-contexts`). Skriptet pekar mot det
  namnet som default (`-KubeContext`).
- **Registry, push-sidan.** `registry.ubk3s:5000` måste finnas i din egen
  hosts-fil (`C:\Windows\System32\drivers\etc\hosts`) och peka på den
  maskin/IP där registryt faktiskt körs. Det är den adress `build.ps1`
  pushar till från din dator.
- **Registry, pull-sidan.** Klustrets noder känner **inte** till
  `.ubk3s`-domänen – den finns bara i din egen hosts-fil. Noderna når
  samma registry via det korta namnet `registry:5000` (Dockers interna DNS
  mellan containrarna i k3d-nätverket). Det är därför push- och
  pull-adressen är två olika parametrar i `build.ps1`
  (`-Registry` respektive `-InClusterRegistry`) – se avsnittet
  "Hosts-fil och registry-DNS – två olika synfält" nedan för varför.
- **Registryt är osäkert (ren HTTP, ingen TLS).** Det räcker inte att bara
  build-maskinen litar på det – **noderna i ubk3s-klustret måste också vara
  konfigurerade att tillåta osäkra/HTTP-registries** (t.ex. via containerd:s
  `config.toml`), annars pushar `dotnet publish` fint från din maskin men
  `kubectl`/kubelet misslyckas ändå med att hämta imagen (`ImagePullBackOff`).
  Detta är redan i ordning i ubk3s, men värt att komma ihåg om ett nytt
  kluster sätts upp.
- **Ingress-controller.** `ingress.yaml` använder `ingressClassName: traefik`
  – ubk3s-home kör Traefik (k3d:s inbyggda default ingress-controller,
  bekräftat via `kubectl get ingressclass`). Stötte på detta i praktiken:
  filen sattes först till `nginx` som en gissning, vilket gav 404 eftersom
  ingen controller kände vid Ingress-objektet (`Address:` förblev tom i
  `kubectl describe ingress`). Sätts ett nytt kluster upp med en annan
  controller måste klassnamnet bytas ut igen – kör
  `kubectl get ingressclass` för att se vad som faktiskt finns.
- **.NET 10 SDK** och **kubectl** installerat lokalt, PowerShell 7+.

### Hosts-fil och registry-DNS – två olika synfält

Innan Ingress-delen: samma grundproblem gäller registryt, och stötte vi
faktiskt på i praktiken (första deployen fick `ImagePullBackOff` med
`dial tcp ...: i/o timeout` när noden försökte nå `registry.ubk3s:5000`).
Orsaken: raden i din hosts-fil gäller bara **din egen Windows-maskin**.
Klustrets noder har sin egen DNS-uppslagning och vet inte vad `.ubk3s` är –
frågan gick vidare till annan DNS och landade fel (troligen din routers IP,
som sedan gav timeout på port 5000 eftersom inget lyssnar där).

Lösningen är att push och pull medvetet använder **olika hostnamn** mot
samma fysiska registry:

- **Push** (från din dator, `build.ps1` → `-Registry`): `registry.ubk3s:5000`
  – kräver din hosts-fil, funkar bara på din maskin.
- **Pull** (från klustrets noder, bakas in i `deployment.yaml` via
  `-InClusterRegistry`): `registry:5000` – det korta namnet som noderna kan
  slå upp direkt via Dockers interna nätverks-DNS i k3d, helt utan att
  `.ubk3s` behöver betyda något för dem.

Image-referensens värdnamn är inget som "fastnar" i själva imagen när den
pushas – det är bara en adress i `deployment.yaml`. Så länge båda namnen
pekar på samma registry-container går det utmärkt att push och pull sker
mot olika hostnamn.

### Hosts-fil kontra Ingress – två olika saker som måste samverka

Det här är lätt att blanda ihop, så här är skillnaden:

- **Hosts-filen** är en lokal, klient-sidig DNS-override på *din* maskin. Den
  talar bara om för din dator vilken IP-adress namnet `simpleapi.ubk3s` ska
  slås upp till (normalt IP:n till klustrets ingress-controller/load
  balancer). Den påverkar bara den maskin där filen finns – ingen annan kan
  nå `simpleapi.ubk3s` utan att ha samma rad i sin egen hosts-fil (eller
  riktig DNS uppsatt).
- **Ingress-resursen** (`ingress.yaml`) lever inne i klustret och gör jobbet
  *efter* att trafiken redan har kommit fram till ingress-controllern. Den
  läser `Host`-headern i den inkommande HTTP-requesten och routar till rätt
  Service baserat på `spec.rules[].host` (här: `simpleapi.ubk3s`).

Med andra ord: hosts-filen ser till att din webbläsare/`curl` överhuvudtaget
skickar requesten till rätt IP, och Ingress-resursen ser till att den
IP:n (ingress-controllern) sedan skickar requesten vidare till rätt
Service/poddar baserat på värdnamnet. Båda måste stämma – rätt IP i
hosts-filen *och* rätt `host:` i Ingress – annars antingen hittar du inte
klustret alls, eller så svarar ingress-controllern med 404 eftersom inget
Ingress-objekt matchar värdnamnet du skickade in.

Samma resonemang gäller `registry.ubk3s` i hosts-filen, fast där finns
ingen Ingress inblandad – det pekar direkt på registry-containerns
IP/port (`:5000`).

## Konfiguration – vad respektive fil gör

### `SimpleApi.csproj`
- `ContainerBaseImage`: `mcr.microsoft.com/dotnet/aspnet:10.0`
- `ContainerPort`: `8081` (bara metadata i imagen – se portavsnittet nedan
  för varför det inte räcker ensamt)
- Registry/repository/tagg sätts **inte** i csproj:en utan skickas in vid
  publish-tillfället av `build.ps1`, så projektfilen förblir
  miljöoberoende.

### Porten – 8080 vs 8081
Bas-avbildningen `aspnet:10.0` lyssnar som standard på port **8080**
(`ASPNETCORE_HTTP_PORTS=8080`), oavsett vad `ContainerPort` i csproj:en
säger – den propertyn är bara `EXPOSE`-metadata, den styr inte vad Kestrel
faktiskt binder till. För att hedra projektets uttalade port 8081 sätts
miljövariabeln `ASPNETCORE_HTTP_PORTS=8081` explicit i
`deployment.yaml`, så att `containerPort`, `targetPort` i Service:n och
probes alla pekar på samma port som appen faktiskt lyssnar på.

### `k8s/namespace.yaml`
Skapar namespace `simpleapi` – allt annat (Service, Ingress, Deployment)
ligger i det namespacet. `build.ps1` applicerar alltid denna fil först.

### `k8s/deployment.yaml`
- 2 repliker, `imagePullPolicy: Always` (rimligt när taggar är unika per
  bygge men imagen ligger på ett internt registry utan digest-pinning).
- `readinessProbe`/`livenessProbe` pekar mot `/weatherforecast` – appen har
  ingen dedikerad health-endpoint, så detta är en pragmatisk lösning, inte
  en idealisk. En riktig `/health`-endpoint (t.ex. via `AddHealthChecks()`)
  vore bättre om ni bygger ut det här senare.
- `resources.requests/limits` satta till små men rimliga värden.
- Image-fältet innehåller två platshållare som `build.ps1` ersätter i minnet
  (aldrig på disk) innan filen pipeas till `kubectl apply -f -`:
  `__REGISTRY_HOST__/simpleapi:__VERSION__` blir t.ex.
  `registry:5000/simpleapi:1.0.3`.
  - `__VERSION__` → versionen som just byggdes. Utan detta skulle en
    hårdkodad tagg göra att `kubectl apply` inte såg någon skillnad mellan
    körningar.
  - `__REGISTRY_HOST__` → `-InClusterRegistry` (default `registry:5000`),
    dvs. den registry-adress klustrets noder faktiskt kan slå upp – se
    "Hosts-fil och registry-DNS" ovan för varför den skiljer sig från
    push-adressen `registry.ubk3s:5000`.
- Ingen separat `Pod`-resurs skapas – poddarna hanteras via Deployment:ens
  pod-mall (`spec.template`), vilket ger självläkning, rullande
  uppdateringar och skalning. En fristående Pod hade saknat allt detta.

### `k8s/service.yaml`
`ClusterIP` på port 80 → `targetPort: http` (8081). Exponeras inte direkt
utåt – all extern trafik går via Ingress.

### `k8s/ingress.yaml`
Routar `simpleapi.ubk3s` (path `/`) till Service:n ovan, port 80.
`ingressClassName: traefik` – se Förutsättningar för bakgrund.

### Medvetna förenklingar
Eftersom målet var en "basic" konfiguration är följande medvetet
uteslutet: dedikerad `/health`-endpoint, `imagePullSecrets` (antar att
registryt är öppet utan autentisering), NetworkPolicies,
HorizontalPodAutoscaler. `UseHttpsRedirection()` i `Program.cs` loggar en
varning i klustret (ingen HTTPS-port att redirecta till, eftersom TLS
termineras vid Ingress) – ofarligt men skräpar ner loggarna; kan tas bort
om ni vill ha renare loggar.

## Versionering

`build.ps1` frågar registryt (`GET http://registry.ubk3s:5000/v2/simpleapi/tags/list`,
Docker Registry HTTP API v2) om vilka taggar som redan finns:

- **Inga tidigare taggar hittas** → version blir `1.0.0`.
- **Taggar hittas** → högsta redan pushade `major.minor.build`-taggen plockas
  ut, och sista siffran (`build`) räknas upp med 1 (t.ex. `1.0.1` → `1.0.2`).
  `major`/`minor` ändras aldrig automatiskt – det görs manuellt vid behov
  genom att pusha en tagg med högre major/minor för hand.

Bara rena tredelade numeriska taggar (`\d+\.\d+\.\d+`) räknas – t.ex.
`latest` ignoreras vid uträkningen.

## Bygg & deploy

Kör från `SimpleApi`-mappens rot:

```powershell
.\build.ps1
```

Detta gör i tur och ordning:

1. Räknar ut nästa version (se ovan).
2. `dotnet publish -t:PublishContainer` – bygger imagen och pushar den till
   `registry.ubk3s:5000/simpleapi:<version>`. `-p:ContainerInsecureRegistries`
   talar om för SDK:n att registryt är HTTP, inte HTTPS.
3. `kubectl --context ubk3s-home apply` av namespace, service och ingress.
4. `kubectl apply` av deployment (med `__VERSION__` utbytt mot den nybyggda
   versionen), pipead via stdin.

### Parametrar

Alla har defaultvärden som matchar den här miljön, men kan overridas:

```powershell
.\build.ps1 -Registry "registry.ubk3s:5000" `
            -InClusterRegistry "registry:5000" `
            -Repository "simpleapi" `
            -KubeContext "ubk3s-home" `
            -ProjectPath ".\SimpleApi\SimpleApi.csproj" `
            -K8sDir ".\k8s"
```

`-SkipDeploy` bygger och pushar imagen men hoppar över `kubectl apply` –
praktiskt om man bara vill bygga en ny version utan att röra klustret än.

```powershell
.\build.ps1 -SkipDeploy
```

## Felsökning

```powershell
# Kör poddarna, och är de Ready?
kubectl --context ubk3s-home -n simpleapi get pods -o wide

# Loggar från appen (t.ex. om den kraschar eller lyssnar på fel port)
kubectl --context ubk3s-home -n simpleapi logs deploy/simpleapi

# Funkar det hela vägen ut via Ingress?
curl http://simpleapi.ubk3s/weatherforecast
```

Vanliga felkällor:

- **`ImagePullBackOff` / `dial tcp ...: i/o timeout`** – stötte vi på i
  praktiken första gången. Antingen (a) `deployment.yaml` pekar på
  `registry.ubk3s:5000` istället för `registry:5000` – `.ubk3s` finns bara i
  din egen hosts-fil, inte i klustrets DNS (se "Hosts-fil och registry-DNS"
  ovan – lösningen är `-InClusterRegistry`), eller (b) noderna litar inte på
  registryt som osäker/HTTP (se Förutsättningar ovan).
- **`0/1 Ready` / probes failar** – kolla loggarna; vanligast är att
  `ASPNETCORE_HTTP_PORTS` av någon anledning inte satt sig och appen lyssnar
  på 8080 istället för 8081.
- **404 från `curl http://simpleapi.ubk3s/...`** – stötte vi på i praktiken:
  `ingressClassName` matchade ingen riktig controller (`nginx` var fel
  gissning, klustret kör Traefik), så Ingress-objektet stod obehandlat
  (`Address:` tom i `kubectl describe ingress`) och något annat svarade 404.
  Kör `kubectl --context ubk3s-home get ingressclass` för att se vad
  klustret faktiskt kör, och `kubectl --context ubk3s-home -n simpleapi
  describe ingress simpleapi` för att se om `Address:` fyllts i. Om
  klassnamnet stämmer men det ändå är 404, kolla istället att hosts-filens
  rad för `simpleapi.ubk3s` pekar på rätt IP (`curl -v` visar vilken IP som
  faktiskt kontaktades).
- **`error: context "..." does not exist`** – kontrollnamnet i
  `-KubeContext` matchar inte namnet i din kubeconfig; kör
  `kubectl config get-contexts` och jämför.
