#requires -version 7.0
<#
    .SYNOPSIS
    Bygger CQRSClient (Angular) till en nginx-image, pushar den till registryt
    och installerar/uppgraderar den i klustret med Helm-charten i ./chart.

    .DESCRIPTION
    Samma upplägg som CQRS\build.ps1:
      - Versionen räknas ut från taggarna i registryt
        (1.0.0 första gången, sedan +1 på sista siffran).
      - $Registry är adressen DIN DATOR pushar till (localhost:5000 via k3d:s
        port-mappning). $InClusterRegistry är adressen klustrets NODER pullar
        från (registry:5000). Samma registry - två namn.
      - Bygget sker helt i Docker (Dockerfile: node -> nginx), så Node behövs
        inte ens lokalt för att bygga imagen.

    .EXAMPLE
    ./build.ps1
    ./build.ps1 -SkipBuild          # bara helm upgrade med senaste version
    ./build.ps1 -Values my.yaml     # egna Helm-values
#>

[CmdletBinding()]
param(
    [string]$Registry = "localhost:5000",
    [string]$InClusterRegistry = "registry:5000",
    [string]$Repository = "cqrsclient",
    [string]$KubeContext = "k3d-home",
    [string]$Namespace = "cqrs",
    [string]$Release = "cqrs-client",
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
    $image = "$Registry/${Repository}:$version"

    Write-Host "==> docker build $image" -ForegroundColor Cyan
    docker build -t $image $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw "docker build misslyckades (exit code $LASTEXITCODE)." }

    Write-Host "==> docker push $image" -ForegroundColor Cyan
    docker push $image
    if ($LASTEXITCODE -ne 0) { throw "docker push misslyckades (exit code $LASTEXITCODE)." }
}

if ($SkipDeploy) {
    Write-Host "==> -SkipDeploy angivet, hoppar över helm." -ForegroundColor Yellow
    return
}

Write-Host "==> helm upgrade --install $Release (namespace $Namespace, context $KubeContext)" -ForegroundColor Cyan
$helmArgs = @(
    "upgrade", "--install", $Release, "$PSScriptRoot/chart/cqrs-client",
    "--kube-context", $KubeContext,
    "--namespace", $Namespace, "--create-namespace",
    "--set", "image.repository=$InClusterRegistry/$Repository",
    "--set", "image.tag=$version"
)
if ($Values) { $helmArgs += @("-f", $Values) }

helm @helmArgs
if ($LASTEXITCODE -ne 0) { throw "helm upgrade misslyckades." }

Write-Host "==> Klart! cqrsclient:$version är deployad." -ForegroundColor Green
Write-Host "    http://cqrsclient.local   (kräver '127.0.0.1 cqrsclient.local' i hosts-filen)"
