#!/usr/bin/env pwsh

# ==============================================================================
# Motsvarigheten till k3s\deploy-infra.ps1, fast för k3d-home.
# Skillnader mot k3s-varianten (se claude/k3d-migrering.md för motivering):
#   - Inget STEG för att mkdir/chmod /data/shared/k8s via bash/sudo -
#     createk3d-home.ps1 har redan skapat C:\data\shared\k8s, och
#     local-path-provisioner skapar sina egna underkataloger vid behov,
#     precis som redan bekräftat fungera på ubk3s-home.
#   - kubeseal laddas ner som windows-amd64-arkivet (inte linux-amd64) och
#     extraheras med Windows inbyggda tar.exe - ingen bash krävs.
#   - INGET kubeconfig-export-steg (motsvarande k3s-variantens STEG 5) -
#     k3d skriver redan en direkt användbar kubeconfig lokalt på samma
#     Windows-maskin (options.kubeconfig.updateDefaultKubeconfig i
#     k3d-install.yaml). Det fanns aldrig något "extern klient"-behov här.
#
#   - (2026-09-25) StorageClass "local-shared-path" fanns bara i det gamla
#     k3s-klustret. I k3d-home används k3s inbyggda "local-path", som via
#     --default-local-storage-path i k3d-install.yaml redan skriver till
#     C:\data\shared\k8s. NATS-values rättade till chartens riktiga nycklar.
#   - Values-filerna läses relativt skriptets mapp ($PSScriptRoot), så
#     skriptet kan köras från vilken katalog som helst.
#
# OBS (2026-09-23): INTE live-testat än.
# ==============================================================================

# ==============================================================================
# KONFIGURATION
# ==============================================================================
$MySQLReleaseName = "mysql"
$MySQLNamespace   = "mysql"
$MySQLPort        = 3306
$MySQLRootPwd     = "SuperSecret123!"

$NATSReleaseName  = "nats"
$NATSNamespace    = "nats"

$RedisReleaseName = "redis"
$RedisNamespace   = "redis"
$RedisRootPwd     = "SuperSecret123!"

$SecretsReleaseName = "sealed-secrets"
$SecretsNamespace   = "sealed-secrets"

# ==============================================================================
# STEG 1: INSTALLERA KUBESEAL LOKALT OM DET SAKNAS (Windows-arkivet)
# ==============================================================================
if (-not (Get-Command kubeseal -ErrorAction SilentlyContinue)) {
    Write-Host "--- Installerar kubeseal CLI (windows-amd64) ---" -ForegroundColor Cyan
    $KUBESEAL_VERSION = "v0.27.0"
    $KubesealAssetVersion = $KUBESEAL_VERSION.TrimStart("v")
    $Url = "https://github.com/bitnami-labs/sealed-secrets/releases/download/$KUBESEAL_VERSION/kubeseal-$KubesealAssetVersion-windows-amd64.tar.gz"
    Invoke-WebRequest -Uri $Url -OutFile "$PSScriptRoot/kubeseal.tar.gz"
    # Windows har haft ett inbyggt tar.exe (bsdtar) sedan Windows 10 1803 -
    # ingen extra installation krävs.
    tar -xzf "$PSScriptRoot/kubeseal.tar.gz" -C "$PSScriptRoot" kubeseal.exe
    Remove-Item "$PSScriptRoot/kubeseal.tar.gz"
    # Lägger bara till skriptets egen mapp i PATH för DENNA session (inte
    # permanent/systemomfattande) - undviker att röra Windows miljövariabler
    # på riktigt. Lägg själv till $PSScriptRoot i din permanenta PATH om du
    # vill kunna köra "kubeseal" i vilket fönster som helst.
    $env:PATH = "$PSScriptRoot;$env:PATH"
    Write-Host "kubeseal.exe hämtad till $PSScriptRoot (tillagd i PATH för den här sessionen)." -ForegroundColor Green
} else {
    Write-Host "--- kubeseal finns redan installerat ---" -ForegroundColor Green
}

# ==============================================================================
# STEG 2: HANTERA HELM REPOSITORIES
# ==============================================================================
Write-Host "--- Uppdaterar Helm-repositorier ---" -ForegroundColor Cyan
helm repo add bitnami "https://charts.bitnami.com/bitnami" --force-update
helm repo add nats "https://nats-io.github.io/k8s/helm/charts/" --force-update
helm repo add sealed-secrets "https://bitnami.github.io/sealed-secrets" --force-update
helm repo update

# ==============================================================================
# STEG 3: INSTALLERA APPLIKATIONER (MED PERSISTENS OCH NAMESPACES)
# ==============================================================================

# 1. Sealed Secrets
Write-Host "--- Deployar Sealed Secrets ---" -ForegroundColor Green
helm upgrade --install $SecretsReleaseName sealed-secrets/sealed-secrets --namespace $SecretsNamespace --create-namespace --set fullnameOverride=sealed-secrets-controller

# 2. MySQL (dynamisk persistens via local-path till C:\data\shared\k8s)
Write-Host "--- Deployar MySQL ---" -ForegroundColor Green
helm upgrade --install $MySQLReleaseName bitnami/mysql --namespace $MySQLNamespace `
 --create-namespace `
 -f "$PSScriptRoot/mysql-values.yaml" `
 --set auth.rootPassword=$MySQLRootPwd `
 --set primary.service.ports.mysql=$MySQLPort

kubectl patch svc mysql -n $MySQLNamespace -p '{"spec":{"type":"LoadBalancer"}}'

# 3. NATS (JetStream, local-path-persistens och LoadBalancer via service.merge)
Write-Host "--- Deployar NATS med JetStream ---" -ForegroundColor Green
helm upgrade --install $NATSReleaseName nats/nats `
  --namespace $NATSNamespace `
  --create-namespace `
  -f "$PSScriptRoot/nats-values.yaml"

# (Ingen kubectl patch behövs längre - nats-values.yaml sätter
# service.merge.spec.type: LoadBalancer, som ServiceLB i k3d-noden plockar upp.)

# 4. Redis (standalone med dynamisk persistens via local-path)
Write-Host "--- Deployar Redis (standalone) ---" -ForegroundColor Green
helm upgrade --install $RedisReleaseName bitnami/redis --namespace $RedisNamespace `
  --create-namespace `
  -f "$PSScriptRoot/redis-values.yaml" `
  --set auth.password=$RedisRootPwd

kubectl patch svc redis-master -n $RedisNamespace -p '{"spec":{"type":"LoadBalancer"}}'

# ==============================================================================
# STEG 4: VERIFIERA STATUS
# ==============================================================================
Write-Host "--- Väntar 5 sekunder på att pods initieras... ---" -ForegroundColor Cyan
Start-Sleep -Seconds 5

$Namespaces = @($SecretsNamespace, $MySQLNamespace, $NATSNamespace, $RedisNamespace)
foreach ($ns in $Namespaces) {
    Write-Host "`n[Pods i namespace: $ns]" -ForegroundColor Yellow
    kubectl get pods -n $ns
}
