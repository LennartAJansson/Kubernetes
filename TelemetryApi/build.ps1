#requires -version 7.0
<#
    .SYNOPSIS
    Bygger TelemetryApi som container-image (dotnet publish, ingen Dockerfile),
    pushar den till registryt och installerar/uppgraderar den i klustret med
    Helm-charten i ./charts/telemetry-api.

    .DESCRIPTION
    Samma upplägg som CQRS\build.ps1:
      - Versionen räknas ut från taggarna i registryt
        (1.0.0 första gången, sedan +1 på sista siffran).
      - $Registry är adressen DIN DATOR pushar till (localhost:5000 via k3d:s
        port-mappning). $InClusterRegistry är adressen klustrets NODER pullar
        från (registry:5000). Samma registry - två namn.

    Förutsättningar: MySQL (Infra\deploy-infra.ps1), databasen
    (./setup-db.ps1) och telemetristacken (Telemetry\deploy-telemetry.ps1).

    .EXAMPLE
    ./build.ps1
    ./build.ps1 -SkipBuild            # bara helm upgrade med senaste version
    ./build.ps1 -Values my.yaml       # egna Helm-values
#>

[CmdletBinding()]
param(
    [string]$Registry = "localhost:5000",
    [string]$InClusterRegistry = "registry:5000",
    [string]$Repository = "telemetryapi",
    [string]$KubeContext = "k3d-home",
    [string]$Namespace = "customers",
    [string]$Release = "telemetry-api",
    [string]$Values = "",
    [switch]$SkipBuild,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"

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

$latest = Get-LatestVersion -Registry $Registry -Repository $Repository

if ($SkipBuild) {
    if (-not $latest) { throw "-SkipBuild angivet men ingen version av $Repository finns i registryt." }
    $version = $latest.ToString()
    Write-Host "==> Hoppar över bygget, använder senaste version $version" -ForegroundColor Yellow
}
else {
    $version = if ($latest) { [System.Version]::new($latest.Major, $latest.Minor, $latest.Build + 1).ToString() } else { "1.0.0" }
    Write-Host "==> Bygger och pushar $Registry/${Repository}:$version" -ForegroundColor Cyan

    dotnet publish "$PSScriptRoot/TelemetryApi/TelemetryApi.csproj" `
        -c Release `
        -t:PublishContainer `
        -p:ContainerRegistry=$Registry `
        -p:ContainerRepository=$Repository `
        -p:ContainerImageTag=$version `
        -p:ContainerInsecureRegistries=$Registry `
        -p:Version=$version
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish misslyckades (exit code $LASTEXITCODE)." }
}

if ($SkipDeploy) {
    Write-Host "==> -SkipDeploy angivet, hoppar över helm." -ForegroundColor Yellow
    return
}

Write-Host "==> helm upgrade --install $Release (namespace $Namespace, context $KubeContext)" -ForegroundColor Cyan
$helmArgs = @(
    "upgrade", "--install", $Release, "$PSScriptRoot/charts/telemetry-api",
    "--kube-context", $KubeContext,
    "--namespace", $Namespace, "--create-namespace",
    "--set", "image.repository=$InClusterRegistry/$Repository",
    "--set", "image.tag=$version"
)
if ($Values) { $helmArgs += @("-f", $Values) }

helm @helmArgs
if ($LASTEXITCODE -ne 0) { throw "helm upgrade misslyckades." }

Write-Host "==> Klart! telemetryapi:$version är deployad." -ForegroundColor Green
Write-Host "    kubectl get pods -n $Namespace --context $KubeContext"
Write-Host "    http://telemetryapi.local/scalar   (kräver '127.0.0.1 telemetryapi.local' i hosts-filen)"
