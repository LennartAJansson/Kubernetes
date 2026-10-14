# =============================================================================
# Labb 7 - facit: skapa användaren lab-user med ett klientcertifikat som
# klustrets CA signerar, och ett kubeconfig-context som använder det.
#
# Samma steg som i presentationen (labb 7a-7c), samlade i ett skript.
# Kör från mappen Labbar\Kvall7 med admin-contextet k3d-home aktivt:
#
#   .\07-create-user.ps1
#   kubectl config use-context lab
#
# Kräver openssl. Det följer med Git for Windows.
# =============================================================================
param(
    [string]$UserName  = "lab-user",
    [string]$Group     = "lab-team",
    [string]$Namespace = "lab",
    [string]$Cluster   = "k3d-home",
    [int]$ValidSeconds = 86400        # 24 h - ett certifikat kan inte återkallas
)
$ErrorActionPreference = "Stop"

# --- openssl: PATH eller Git for Windows ------------------------------------
$openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
if (-not $openssl) { $openssl = "C:\Program Files\Git\usr\bin\openssl.exe" }
if (-not (Test-Path $openssl)) { throw "Hittar inte openssl. Installera Git for Windows eller lägg openssl i PATH." }

# Ska köras som admin - kontrollera contextet innan vi börjar
$current = kubectl config current-context
if ($current -ne $Cluster) { throw "Aktivt context är '$current'. Kör: kubectl config use-context $Cluster" }

$key = Join-Path $PWD "$UserName.key"
$csr = Join-Path $PWD "$UserName.csr"
$crt = Join-Path $PWD "$UserName.crt"

# --- 1. Privat nyckel + Certificate Signing Request (labb 7a) ---------------
Write-Host "1. Skapar nyckel och CSR för CN=$UserName, O=$Group" -ForegroundColor Cyan
& $openssl genrsa -out $key 2048
& $openssl req -new -key $key -out $csr -subj "/CN=$UserName/O=$Group"
if ($LASTEXITCODE -ne 0) { throw "openssl req misslyckades" }

# --- 2. Låt klustret signera (labb 7b) --------------------------------------
Write-Host "2. Skickar CSR till klustret och godkänner" -ForegroundColor Cyan
$request = [Convert]::ToBase64String([IO.File]::ReadAllBytes($csr))
kubectl delete csr $UserName --ignore-not-found | Out-Null
@"
apiVersion: certificates.k8s.io/v1
kind: CertificateSigningRequest
metadata:
  name: $UserName
spec:
  request: $request
  signerName: kubernetes.io/kube-apiserver-client
  expirationSeconds: $ValidSeconds
  usages: ["client auth"]
"@ | kubectl apply -f -
kubectl certificate approve $UserName

# Certifikatet dyker upp i status när det är signerat
$cert = ""
for ($i = 0; $i -lt 10 -and -not $cert; $i++) {
    Start-Sleep -Seconds 1
    $cert = kubectl get csr $UserName -o jsonpath='{.status.certificate}'
}
if (-not $cert) { throw "CSR:en signerades inte - kontrollera: kubectl describe csr $UserName" }
[IO.File]::WriteAllBytes($crt, [Convert]::FromBase64String($cert))
& $openssl x509 -in $crt -noout -subject -issuer -enddate

# --- 3. Rättigheter + kubeconfig (labb 7c) ----------------------------------
Write-Host "3. Role, RoleBinding och context" -ForegroundColor Cyan
kubectl create namespace $Namespace --dry-run=client -o yaml | kubectl apply -f -
kubectl apply -f (Join-Path $PSScriptRoot "07-namespace-user.yaml")

kubectl config set-credentials $UserName --client-certificate=$crt --client-key=$key --embed-certs=true
kubectl config set-context lab --cluster=$Cluster --user=$UserName --namespace=$Namespace

Write-Host ""
Write-Host "Klart. Testa:" -ForegroundColor Green
Write-Host "  kubectl config use-context lab"
Write-Host "  kubectl get pods                  # fungerar"
Write-Host "  kubectl get pods -n kube-system   # Forbidden"
Write-Host "  kubectl config use-context $Cluster   # tillbaka som admin"
