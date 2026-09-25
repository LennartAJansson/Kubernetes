#!/usr/bin/env pwsh

# ==============================================================================
# Motsatsen till deploy-infra.ps1: river allt som det skriptet installerar, så
# att en ändring (lösenord, values-filer, lagring ...) kan köras om från noll.
#
#   ./remove-infra.ps1                     # frågar först, river allt
#   ./remove-infra.ps1 -Force              # ingen fråga
#   ./remove-infra.ps1 -KeepSealedSecrets  # behåll Sealed Secrets-nyckeln
#
# Per tjänst: helm uninstall -> PVC:erna tas bort -> namespacet tas bort.
# PVC:erna MÅSTE bort: MySQL och Redis läser root-lösenordet bara när
# datavolymen skapas första gången. Ligger den gamla volymen kvar gäller det
# gamla lösenordet, oavsett vad deploy-infra.ps1 säger.
#
# local-path har reclaimPolicy Delete, så när PVC:n tas bort raderas även
# datat i C:\data\shared\k8s\pvc-... automatiskt.
#
# OBS: Sealed Secrets-controllerns nyckel ligger i en Secret i dess namespace.
# Rivs den går befintliga SealedSecrets inte längre att dekryptera - de måste
# krypteras om med den nya nyckeln. Använd -KeepSealedSecrets för att behålla den.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$KubeContext = "k3d-home",
    [switch]$KeepSealedSecrets,
    [switch]$Force
)

# ==============================================================================
# KONFIGURATION - samma namn som i deploy-infra.ps1
# ==============================================================================
$Releases = @(
    @{ Name = "redis";          Namespace = "redis" },
    @{ Name = "nats";           Namespace = "nats" },
    @{ Name = "mysql";          Namespace = "mysql" }
)
if (-not $KeepSealedSecrets) {
    $Releases += @{ Name = "sealed-secrets"; Namespace = "sealed-secrets" }
}

# ==============================================================================
# STEG 0: BEKRÄFTA
# ==============================================================================
Write-Host "Kontext: $KubeContext" -ForegroundColor Yellow
Write-Host "Följande tas bort, INKLUSIVE all data i volymerna:" -ForegroundColor Yellow
foreach ($r in $Releases) { Write-Host ("  - {0} (namespace {1})" -f $r.Name, $r.Namespace) }
if ($KeepSealedSecrets) { Write-Host "  (Sealed Secrets behålls)" -ForegroundColor Green }

if (-not $Force) {
    $answer = Read-Host "Skriv 'ja' för att fortsätta"
    if ($answer -ne "ja") { Write-Host "Avbrutet - inget har ändrats."; return }
}

# ==============================================================================
# STEG 1: HELM UNINSTALL
# ==============================================================================
foreach ($r in $Releases) {
    Write-Host "--- helm uninstall $($r.Name) -n $($r.Namespace) ---" -ForegroundColor Cyan
    helm uninstall $r.Name --namespace $r.Namespace --kube-context $KubeContext --wait --ignore-not-found
}

# ==============================================================================
# STEG 2: PVC:ER (helm tar inte bort volymer som StatefulSets har skapat)
# ==============================================================================
foreach ($r in $Releases) {
    Write-Host "--- Tar bort PVC:er i $($r.Namespace) ---" -ForegroundColor Cyan
    kubectl delete pvc --all --namespace $r.Namespace --context $KubeContext --ignore-not-found --wait=true
}

# ==============================================================================
# STEG 3: NAMESPACES (tar med sig det som eventuellt blivit kvar, t.ex. Secrets)
# ==============================================================================
foreach ($r in $Releases) {
    Write-Host "--- Tar bort namespace $($r.Namespace) ---" -ForegroundColor Cyan
    kubectl delete namespace $r.Namespace --context $KubeContext --ignore-not-found --wait=true
}

# ==============================================================================
# STEG 4: VERIFIERA
# ==============================================================================
Write-Host "`n--- Kvarvarande PVC:er i klustret (ska inte visa mysql/nats/redis) ---" -ForegroundColor Cyan
kubectl get pvc -A --context $KubeContext
Write-Host "`n--- Kvarvarande PV:er (ska tömmas inom några sekunder) ---" -ForegroundColor Cyan
kubectl get pv --context $KubeContext

Write-Host "`nKlart. Kör ./deploy-infra.ps1 för att installera på nytt." -ForegroundColor Green
Write-Host "OBS: databasen 'cqrs' och dess användare är borta - kör CQRS\setup-db.ps1 igen efteråt." -ForegroundColor Yellow
