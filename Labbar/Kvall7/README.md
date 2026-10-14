# Labbar kväll 7 - konfiguration, hemligheter och drift

Alla labbar körs i namespacet `lab`. Städa efteråt med `kubectl delete namespace lab`.

| Fil | Labb |
|---|---|
| `01-configmap-demo.yaml` | ConfigMap som miljövariabler och som fil |
| `02-secret-demo.yaml` | Secret som miljövariabel och fil - och senare via SealedSecret |
| `06-hpa-demo.yaml` | Automatisk skalning med HorizontalPodAutoscaler |
| `07-namespace-user.yaml` | Role + RoleBinding som låser användaren `lab-user` till namespacet `lab` |
| `07-create-user.ps1` | Facit för labb 7: klientcertifikat (openssl + CertificateSigningRequest) och context `lab` |

kubeseal finns i `D:\Kvällskurs\Infra` (hämtades av `deploy-infra.ps1`). Lägg till mappen i PATH för fönstret:

```powershell
$env:PATH = "D:\Kvällskurs\Infra;$env:PATH"
kubeseal --version
```

Sealed Secrets-controllern ligger i namespacet `sealed-secrets` med namnet
`sealed-secrets-controller`. kubeseal letar som standard i `kube-system`,
därför används flaggan `--controller-namespace sealed-secrets` i alla labbar.

Alla kommandon står i presentationen *Kubernetes - Konfiguration, hemligheter och drift*.

## Labb 7 - användarcertifikat

openssl följer med Git for Windows. Finns det inte i PATH:

```powershell
Set-Alias openssl "C:\Program Files\Git\usr\bin\openssl.exe"
```

Namespacet `lab` måste finnas (skapas i labb 1). Certifikatet gäller i 24 timmar
och kan inte återkallas - ta bort RoleBindingen för att stänga ute användaren.
Byt tillbaka till admin med `kubectl config use-context k3d-home`.
