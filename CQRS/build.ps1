#requires -version 7.0
<#
    .SYNOPSIS
    Bygger CQRSApi och CQRSWorker som container-images (dotnet publish, ingen
    Dockerfile), pushar dem till registryt och installerar/uppgraderar dem i
    klustret med sina Helm-charts.

    .DESCRIPTION
    Samma upplägg som SimpleApi/build.ps1:
      - Versionen räknas ut per image från taggarna i registryt
        (1.0.0 första gången, sedan +1 på sista siffran).
      - $Registry är adressen DIN DATOR pushar till (localhost:5000 via k3d:s
        port-mappning). $InClusterRegistry är adressen klustrets NODER pullar
        från (registry:5000). Samma registry - två namn.
      - Helm-charts i ./charts installeras med `helm upgrade --install`, så
        skriptet fungerar både första gången och vid varje ny version.
        Connection strings, NATS-url och CORS kommer från chartens values.yaml
        (eller från -ApiValues / -WorkerValues om du anger egna filer).

    Förutsättning: MySQL och NATS (JetStream) finns i klustret - se README.md.

    .EXAMPLE
    ./build.ps1
    ./build.ps1 -Only api
    ./build.ps1 -ApiValues ./my-api-values.yaml -SkipBuild
#>

[CmdletBinding()]
param(
    [string]$Registry = "localhost:5000",
    [string]$InClusterRegistry = "registry:5000",
    [string]$KubeContext = "k3d-home",
    [string]$Namespace = "cqrs",
    [ValidateSet("all", "api", "worker")]
    [string]$Only = "all",
    [string]$ApiValues = "",
    [string]$WorkerValues = "",
    [switch]$SkipBuild,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"

$apps = @(
    @{ Key = "worker"; Repository = "cqrsworker"; Project = "$PSScriptRoot/CQRSWorker/CQRSWorker.csproj"; Chart = "$PSScriptRoot/charts/cqrs-worker"; Release = "cqrs-worker"; Values = $WorkerValues },
    @{ Key = "api";    Repository = "cqrsapi";    Project = "$PSScriptRoot/CQRSApi/CQRSApi.csproj";       Chart = "$PSScriptRoot/charts/cqrs-api";    Release = "cqrs-api";    Values = $ApiValues }
) | Where-Object { $Only -eq "all" -or $_.Key -eq $Only }

function Get-LatestVersion {
    param([string]$Registry, [string]$Repository)
    try {
        $response = Invoke-RestMethod -Uri "http://$Registry/v2/$Repository/tags/list" -TimeoutSec 10
        $versions = @($response.tags | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | ForEach-Object { [System.Version]$_ })
        if ($versions.Count -gt 0) { return ($versions | Sort-Object -Descending | Select-Object -First 1) }
    }
    catch {
        Write-Warning "Inga taggar för $Repository i $Registry ännu ($($_.Exception.Message))."
    }
    return $null
}

foreach ($app in $apps) {
    $latest = Get-LatestVersion -Registry $Registry -Repository $app.Repository

    if ($SkipBuild) {
        if (-not $latest) { throw "-SkipBuild angivet men ingen version av $($app.Repository) finns i registryt." }
        $version = $latest.ToString()
        Write-Host "==> [$($app.Key)] Hoppar över bygget, använder senaste version $version" -ForegroundColor Yellow
    }
    else {
        $version = if ($latest) { [System.Version]::new($latest.Major, $latest.Minor, $latest.Build + 1).ToString() } else { "1.0.0" }
        Write-Host "==> [$($app.Key)] Bygger och pushar $Registry/$($app.Repository):$version" -ForegroundColor Cyan

        dotnet publish $app.Project `
            -c Release `
            -t:PublishContainer `
            -p:ContainerRegistry=$Registry `
            -p:ContainerRepository=$($app.Repository) `
            -p:ContainerImageTag=$version `
            -p:ContainerInsecureRegistries=$Registry
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish av $($app.Key) misslyckades (exit code $LASTEXITCODE)." }
    }

    if ($SkipDeploy) { continue }

    Write-Host "==> [$($app.Key)] helm upgrade --install $($app.Release) (namespace $Namespace, context $KubeContext)" -ForegroundColor Cyan
    $helmArgs = @(
        "upgrade", "--install", $app.Release, $app.Chart,
        "--kube-context", $KubeContext,
        "--namespace", $Namespace, "--create-namespace",
        "--set", "image.repository=$InClusterRegistry/$($app.Repository)",
        "--set", "image.tag=$version"
    )
    if ($app.Values) { $helmArgs += @("-f", $app.Values) }

    helm @helmArgs
    if ($LASTEXITCODE -ne 0) { throw "helm upgrade av $($app.Release) misslyckades." }
}

Write-Host "==> Klart!" -ForegroundColor Green
if (-not $SkipDeploy) {
    Write-Host "    kubectl get pods -n $Namespace --context $KubeContext"
    Write-Host "    http://cqrsapi.local/persons   (kräver '127.0.0.1 cqrsapi.local' i hosts-filen)"
}
