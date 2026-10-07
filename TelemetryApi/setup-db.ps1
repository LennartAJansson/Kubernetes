#requires -version 7.0
<#
    .SYNOPSIS
    Skapar databasen "customers" och användaren customers_app i MySQL-instansen
    som Infra\deploy-infra.ps1 har installerat (bitnami/mysql, release "mysql"
    i namespace "mysql").

    .DESCRIPTION
    Idempotent (CREATE ... IF NOT EXISTS + ALTER USER) - kan köras flera gånger.
    Lösenordet måste stämma med connection string i
    charts/telemetry-api/values.yaml och TelemetryApi/appsettings.json.
    Tabellen Customers skapar API:t själv när det startar (EnsureCreated).

    .EXAMPLE
    ./setup-db.ps1
#>

[CmdletBinding()]
param(
    [string]$KubeContext  = "k3d-home",
    [string]$Namespace    = "mysql",
    [string]$Pod          = "mysql-0",
    [string]$Container    = "mysql",
    [string]$RootPassword = "SuperSecret123!",   # = $MySQLRootPwd i Infra\deploy-infra.ps1
    [string]$Database     = "customers",
    [string]$AppPassword  = "customers-pwd"
)

$ErrorActionPreference = "Stop"

$sql = @"
CREATE DATABASE IF NOT EXISTS ``$Database``;

CREATE USER IF NOT EXISTS 'customers_app'@'%' IDENTIFIED BY '$AppPassword';
ALTER USER 'customers_app'@'%' IDENTIFIED BY '$AppPassword';
GRANT ALL PRIVILEGES ON ``$Database``.* TO 'customers_app'@'%';

FLUSH PRIVILEGES;
"@

Write-Host "==> Väntar på att $Namespace/$Pod är redo..." -ForegroundColor Cyan
kubectl --context $KubeContext wait pod/$Pod -n $Namespace --for=condition=Ready --timeout=180s
if ($LASTEXITCODE -ne 0) { throw "$Namespace/$Pod blev inte Ready. Har Infra\deploy-infra.ps1 körts?" }

Write-Host "==> Skapar databasen '$Database' och användaren customers_app..." -ForegroundColor Cyan
$sql | kubectl --context $KubeContext exec -i $Pod -n $Namespace -c $Container -- `
    env MYSQL_PWD=$RootPassword mysql -uroot
if ($LASTEXITCODE -ne 0) { throw "SQL-skriptet misslyckades." }

"SHOW GRANTS FOR 'customers_app'@'%';" |
    kubectl --context $KubeContext exec -i $Pod -n $Namespace -c $Container -- `
        env MYSQL_PWD=$RootPassword mysql -uroot --table

Write-Host "==> Klart! TelemetryApi skapar tabellen Customers när det startar." -ForegroundColor Green
