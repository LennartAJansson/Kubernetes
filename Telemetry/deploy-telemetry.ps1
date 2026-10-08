#!/usr/bin/env pwsh

# ==============================================================================
# Telemetristacken för k3d-home: Tempo, Loki, Prometheus, Grafana,
# OpenTelemetry Collector och Faro Collector i namespace "monitoring".
#
# Kör från valfri mapp - alla filer läses relativt skriptets egen mapp.
#
# RÄTTAT 2026-10-04 (filerna var inte live-testade):
#   - storageClass local-shared-path -> local-path i alla values-filer
#     (samma Pending-problem som Infra hade).
#   - otel-collectorn skickade till "tempo.monitoring" - Servicen heter
#     telemetry-tempo.
#   - Prometheus skrapade inte collectorns metrics-port - annotationer
#     tillagda på otel-collector-Servicen.
#   - Lokis memcached-cachar (ca 10 GB minnesbegäran) avstängda.
#   - Steg 2 skapar nu själv TLS-hemligheten med mkcert. Tidigare hänvisade
#     traefik-tls.yaml till createk3d-home.ps1, som inte finns i k3d-mappen.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$KubeContext = "k3d-home",
    [string]$Namespace = "monitoring",
    [string]$CertDir = "C:\data\shared\certs",
    [switch]$SkipTls
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    kubectl config use-context $KubeContext | Out-Null

    # ==========================================================================
    # STEG 1: HELM REPOSITORIES OCH NAMESPACE
    # ==========================================================================
    Write-Host "--- Uppdaterar Helm-repositorier för Telemetri ---" -ForegroundColor Cyan
    helm repo add grafana "https://grafana.github.io/helm-charts" --force-update
    helm repo add prometheus-community "https://prometheus-community.github.io/helm-charts" --force-update
    helm repo update

    kubectl create namespace $Namespace --dry-run=client -o yaml | kubectl apply -f -

    # ==========================================================================
    # STEG 2: CERTIFIKAT OCH GLOBAL TLS FÖR TRAEFIK
    #
    # mkcert skapar en egen rot-CA som Windows litar på, och ett certifikat
    # för *.local. Hemligheten local-tls-secret i kube-system används sedan
    # av alla HTTPS-ingresser via TLSStore "default" (traefik-tls.yaml).
    # Installera mkcert en gång:  winget install FiloSottile.mkcert
    # ==========================================================================
    if (-not $SkipTls) {
        Write-Host "--- Konfigurerar certifikat och global TLSStore för Traefik ---" -ForegroundColor Cyan
        kubectl get secret local-tls-secret -n kube-system 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    local-tls-secret finns redan i kube-system - hoppar över mkcert." -ForegroundColor DarkGray
        }
        elseif (Get-Command mkcert -ErrorAction SilentlyContinue) {
            New-Item -ItemType Directory -Force -Path $CertDir | Out-Null
            mkcert -install
            mkcert -cert-file "$CertDir\tls.crt" -key-file "$CertDir\tls.key" `
                "*.local" grafana.local prometheus.local loki.local tempo.local otel.local otel-grpc.local faro.local
            if ($LASTEXITCODE -ne 0) { throw "mkcert misslyckades." }

            kubectl create secret tls local-tls-secret `
                --cert="$CertDir\tls.crt" --key="$CertDir\tls.key" -n kube-system
            if ($LASTEXITCODE -ne 0) { throw "Kunde inte skapa local-tls-secret." }
        }
        else {
            Write-Warning "mkcert hittades inte. Installera med 'winget install FiloSottile.mkcert', öppna en ny terminal och kör skriptet igen."
            Write-Warning "Fortsätter utan eget certifikat - https://*.local får Traefiks självsignerade certifikat och webbläsaren varnar."
        }
        kubectl apply -f traefik-tls.yaml
    }

    # ==========================================================================
    # STEG 3: STACKEN VIA HELM (MED PERSISTENS I local-path)
    # ==========================================================================
    Write-Host "--- Deployar Tempo via Helm ---" -ForegroundColor Green
    helm upgrade --install telemetry-tempo grafana/tempo --namespace $Namespace -f tempo-values.yaml
    if ($LASTEXITCODE -ne 0) { throw "helm: telemetry-tempo misslyckades." }

    Write-Host "--- Deployar Loki via Helm ---" -ForegroundColor Green
    helm upgrade --install telemetry-loki grafana/loki --namespace $Namespace -f loki-values.yaml
    if ($LASTEXITCODE -ne 0) { throw "helm: telemetry-loki misslyckades." }

    Write-Host "--- Deployar Prometheus via Helm ---" -ForegroundColor Green
    helm upgrade --install telemetry-prometheus prometheus-community/prometheus --namespace $Namespace -f prometheus-values.yaml
    if ($LASTEXITCODE -ne 0) { throw "helm: telemetry-prometheus misslyckades." }

    Write-Host "--- Deployar Grafana via Helm ---" -ForegroundColor Green
    helm upgrade --install telemetry-grafana grafana/grafana --namespace $Namespace -f grafana-values.yaml
    if ($LASTEXITCODE -ne 0) { throw "helm: telemetry-grafana misslyckades." }

    # ==========================================================================
    # STEG 4: INSAMLARE OCH INGRESSER
    # ==========================================================================
    Write-Host "--- Driftsätter OTel, Faro och Ingress-regler ---" -ForegroundColor Cyan
    kubectl apply -f otel-collector.yaml
    kubectl apply -f faro-collector.yaml
    kubectl apply -f ingress.yaml

    # Dashboards som kod (kväll 6) - se deploy-dashboards.ps1.
    Write-Host "--- Läser in dashboards ---" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "deploy-dashboards.ps1") -KubeContext $KubeContext -Namespace $Namespace

    # ==========================================================================
    # STEG 5: VERIFIERA
    # ==========================================================================
    Write-Host "`n--- Väntar på att poddarna blir Ready (max 5 min) ---" -ForegroundColor Cyan
    kubectl wait pod --all -n $Namespace --for=condition=Ready --timeout=300s

    Write-Host "`n[Pods i namespace: $Namespace]" -ForegroundColor Yellow
    kubectl get pods -n $Namespace
    Write-Host "`n[PVC:er - alla ska vara Bound]" -ForegroundColor Yellow
    kubectl get pvc -n $Namespace

    Write-Host "`nKlart! Lägg till i hosts-filen (som administratör):" -ForegroundColor Green
    Write-Host "  127.0.0.1 grafana.local prometheus.local loki.local tempo.local otel.local otel-grpc.local faro.local"
    Write-Host "Grafana: https://grafana.local  (admin / admin)"
    Write-Host "Översikt: https://grafana.local/d/telemetri-oversikt"
}
finally {
    Pop-Location
}
