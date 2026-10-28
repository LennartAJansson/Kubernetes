#requires -version 7.0
<#
    .SYNOPSIS
    Visar namn (SAN), utfärdare och giltighet för ett certifikat - antingen en
    .crt-fil eller certifikatet som en HTTPS-server svarar med.

    .DESCRIPTION
    Ersätter openssl i kväll 8. För ett värdnamn görs en TLS-handskakning precis
    som i en webbläsaren: värdnamnet skickas som SNI, så Traefik väljer
    certifikat efter det. Certifikatet kontrolleras inte - skriptet visar bara
    vad servern svarar med.

    .EXAMPLE
    .\cert-info.ps1 telemetryapi.local      # certifikatet servern svarar med
    .\cert-info.ps1 telemetryapi.crt        # en certifikatfil
    (.\cert-info.ps1 grafana.local).NotAfter
#>
param(
    [Parameter(Mandatory)][string]$Target,
    [int]$Port = 443
)
$ErrorActionPreference = "Stop"
$X509 = [System.Security.Cryptography.X509Certificates.X509Certificate2]

if (Test-Path $Target) {
    $cert = $X509::new((Resolve-Path $Target).Path)
}
else {
    $tcp = [System.Net.Sockets.TcpClient]::new($Target, $Port)
    try {
        # { $true } = acceptera certifikatet oavsett - vi vill bara titta på det
        $ssl = [System.Net.Security.SslStream]::new($tcp.GetStream(), $false, { $true })
        $ssl.AuthenticateAsClient($Target)      # $Target skickas som SNI
        $cert = $X509::new($ssl.RemoteCertificate)
    }
    finally {
        $tcp.Dispose()
    }
}

# 2.5.29.17 = Subject Alternative Name, namnen certifikatet gäller för
$san = $cert.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | ForEach-Object { $_.Format($false) }

[pscustomobject]@{
    Names     = $san
    Issuer    = $cert.Issuer
    NotBefore = $cert.NotBefore
    NotAfter  = $cert.NotAfter
}
