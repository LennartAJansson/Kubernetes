#requires -version 7.0
<#
    .SYNOPSIS
    Bygger SimpleApi som en container-image (via .NET SDK:ns inbyggda
    containerisering, ingen Dockerfile), versionssätter den, pushar till
    registry.ubk3s:5000 och applicerar Kubernetes-konfigurationen i ./k8s.

    .DESCRIPTION
    Version:
      - Om inga taggar hittas i registryt för repot "simpleapi" sätts
        versionen till 1.0.0 (samma tredelade format som i Microsofts egna
        exempel, t.ex. 1.2.3).
      - Annars plockas den högsta redan pushade major.minor.build-taggen och
        sista siffran (build) räknas upp med 1. Major och minor ändras
        aldrig automatiskt.

    Deploy:
      - Allt körs i namespace "simpleapi" (samma namn som applikationen).
        namespace.yaml appliceras alltid först.
      - deployment.yaml innehåller platshållarna __VERSION__ och
        __REGISTRY_HOST__ i image-referensen. De ersätts i minnet
        (källfilen ändras inte på disk) innan kubectl apply:
          * __VERSION__        -> versionen som just byggdes (se ovan)
          * __REGISTRY_HOST__  -> $InClusterRegistry (default "registry:5000")
        OBS: $Registry (push-adressen, "registry.ubk3s:5000") och
        $InClusterRegistry (pull-adressen, "registry:5000") är MEDVETET
        olika. ".ubk3s" är ett domännamn som bara finns i din egen
        hosts-fil på din dator – klustrets noder känner inte till det och
        får ImagePullBackOff om deployment.yaml hårdkodar den adressen.
        Noderna kan däremot slå upp det korta namnet "registry" (Dockers
        interna DNS mellan containrarna i k3d-nätverket). Se README.md.
      - namespace.yaml, service.yaml och ingress.yaml ändras aldrig och
        appliceras som de är.
#>

[CmdletBinding()]
param(
    # Adressen build.ps1 pushar till FRÅN DIN DATOR. Kräver att
    # registry.ubk3s finns i din egen hosts-fil.
    [string]$Registry = "registry.local:5000",

    # Adressen klustrets NODER använder för att pulla samma image. ".ubk3s"
    # finns bara i din egen hosts-fil, inte i klustrets DNS – noderna når
    # dock samma registry via det korta namnet "registry" (Dockers interna
    # DNS i k3d-nätverket). Se README.md.
    [string]$InClusterRegistry = "registry:5000",

    [string]$Repository = "simpleapi",
    [string]$KubeContext = "k3d-local-hp",
    [string]$ProjectPath = "$PSScriptRoot/SimpleApi/SimpleApi.csproj",
    [string]$K8sDir = "$PSScriptRoot/k8s",
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"

function Get-NextVersion {
    param(
        [string]$Registry,
        [string]$Repository
    )

    $tagsUrl = "http://$Registry/v2/$Repository/tags/list"
    $tags = @()

    try {
        $response = Invoke-RestMethod -Uri $tagsUrl -Method Get -TimeoutSec 10
        if ($response.tags) {
            $tags = $response.tags
        }
    }
    catch {
        # Vanligast: repot finns inte än (första bygget) eller registryt
        # svarar inte just nu. Vi loggar men behandlar det som "inga taggar
        # hittades" och startar om på 1.0.0 nedan.
        Write-Warning "Kunde inte hämta tagg-listan från $tagsUrl (`$($_.Exception.Message)`). Antar att inga images finns ännu."
    }

    $versions = @()
    foreach ($tag in $tags) {
        # Bara rena tredelade taggar (1.0.0, 1.2.3, ...) räknas som en
        # tidigare version av just den här appen – t.ex. "latest" eller
        # andra taggar ignoreras.
        if ($tag -notmatch '^\d+\.\d+\.\d+$') {
            continue
        }
        $parsed = $null
        if ([System.Version]::TryParse($tag, [ref]$parsed)) {
            $versions += $parsed
        }
    }

    if ($versions.Count -eq 0) {
        return [System.Version]::new(1, 0, 0)
    }

    $latest = $versions | Sort-Object -Descending | Select-Object -First 1
    return [System.Version]::new($latest.Major, $latest.Minor, $latest.Build + 1)
}

Write-Host "==> Räknar ut nästa version från $Registry/$Repository..." -ForegroundColor Cyan
$version = Get-NextVersion -Registry $Registry -Repository $Repository
$versionString = $version.ToString()
Write-Host "==> Bygger version $versionString" -ForegroundColor Cyan

Write-Host "==> Publicerar och pushar container-image..." -ForegroundColor Cyan
dotnet publish $ProjectPath `
    -c Release `
    -t:PublishContainer `
    -p:ContainerRegistry=$Registry `
    -p:ContainerRepository=$Repository `
    -p:ContainerImageTag=$versionString `
    -p:ContainerInsecureRegistries=$Registry

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish misslyckades (exit code $LASTEXITCODE). Avbryter – inget deployas."
}

if ($SkipDeploy) {
    Write-Host "==> -SkipDeploy angivet, hoppar över kubectl apply." -ForegroundColor Yellow
    return
}

Write-Host "==> Applicerar Kubernetes-konfiguration (context: $KubeContext)..." -ForegroundColor Cyan

# namespace.yaml måste appliceras (och finnas) innan resten, eftersom
# service/ingress/deployment nedan ligger i namespace "simpleapi".
kubectl --context $KubeContext apply -f "$K8sDir/namespace.yaml"
if ($LASTEXITCODE -ne 0) { throw "kubectl apply av namespace.yaml misslyckades." }

# service.yaml och ingress.yaml är statiska och appliceras direkt.
kubectl --context $KubeContext apply -f "$K8sDir/service.yaml"
if ($LASTEXITCODE -ne 0) { throw "kubectl apply av service.yaml misslyckades." }

kubectl --context $KubeContext apply -f "$K8sDir/ingress.yaml"
if ($LASTEXITCODE -ne 0) { throw "kubectl apply av ingress.yaml misslyckades." }

# deployment.yaml innehåller platshållarna __VERSION__ och
# __REGISTRY_HOST__ – ersätt dem i minnet och skicka in via stdin, så att
# (a) den version som byggdes ovan är den som faktiskt rullas ut, och
# (b) noderna pullar imagen via en adress de faktiskt kan slå upp
# ($InClusterRegistry), inte push-adressen ($Registry) som bara finns i
# din egen hosts-fil. Filen på disk lämnas orörd.
$deploymentYaml = Get-Content -Path "$K8sDir/deployment.yaml" -Raw
$deploymentYaml = $deploymentYaml.Replace("__VERSION__", $versionString)
$deploymentYaml = $deploymentYaml.Replace("__REGISTRY_HOST__", $InClusterRegistry)
$deploymentYaml | kubectl --context $KubeContext apply -f -
if ($LASTEXITCODE -ne 0) { throw "kubectl apply av deployment.yaml misslyckades." }

Write-Host "==> Klart! simpleapi:$versionString är byggd, pushad och applicerad mot $KubeContext." -ForegroundColor Green
