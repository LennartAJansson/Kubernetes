#requires -version 7.0
<#
    .SYNOPSIS
    Läser in dashboards i Grafana från JSON-filerna i ./dashboards.

    .DESCRIPTION
    Dashboards som kod: varje JSON-fil blir en ConfigMap i namespace monitoring
    med labeln grafana_dashboard=1. Grafanas sidecar (se grafana-values.yaml)
    hittar dem och läser in dem i mappen "Kvällskurs" - inom någon minut, utan
    omstart.

    Arbetsflöde när man byggt en dashboard i Grafana:
      Dashboard -> Export -> Export as JSON -> spara i ./dashboards -> kör skriptet.

    .EXAMPLE
    ./deploy-dashboards.ps1
#>

[CmdletBinding()]
param(
    [string]$KubeContext = "k3d-home",
    [string]$Namespace = "monitoring",
    [string]$Folder = "Kvällskurs"
)

$ErrorActionPreference = "Stop"

Get-ChildItem -Path (Join-Path $PSScriptRoot "dashboards") -Filter *.json | ForEach-Object {
    $name = "grafana-dashboard-" + $_.BaseName.ToLowerInvariant()
    Write-Host "==> $($_.Name) -> ConfigMap $name" -ForegroundColor Cyan

    # create --dry-run | apply: skapar första gången, uppdaterar sedan.
    kubectl --context $KubeContext create configmap $name -n $Namespace `
        --from-file="$($_.Name)=$($_.FullName)" --dry-run=client -o yaml |
        kubectl --context $KubeContext apply -f -
    if ($LASTEXITCODE -ne 0) { throw "Kunde inte skapa ConfigMap $name." }

    kubectl --context $KubeContext label configmap $name -n $Namespace grafana_dashboard=1 --overwrite | Out-Null
    kubectl --context $KubeContext annotate configmap $name -n $Namespace "grafana_folder=$Folder" --overwrite | Out-Null
}

Write-Host "==> Klart! Dashboards dyker upp i Grafana under Dashboards -> $Folder inom någon minut." -ForegroundColor Green
Write-Host "    https://grafana.local/d/telemetri-oversikt"
