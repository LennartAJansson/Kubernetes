#requires -version 7.0
<#
    .SYNOPSIS
    Skapar databasen och de två databasanvändarna som CQRS-demon behöver, i
    MySQL-instansen som deploy-infra.ps1 har installerat (bitnami/mysql,
    release "mysql" i namespace "mysql").

    .DESCRIPTION
    Två användare speglar CQRS-uppdelningen även i rättigheterna:
      cqrs_writer - ALLA rättigheter på databasen cqrs -> CQRSWorker (EF Core)
      cqrs_reader - bara SELECT                        -> CQRSApi   (Dapper)

    Skriptet är idempotent (CREATE ... IF NOT EXISTS + ALTER USER), så det kan
    köras hur många gånger som helst - t.ex. för att byta lösenord.

    Lösenorden här måste stämma med connection strings i
    charts/cqrs-worker/values.yaml och charts/cqrs-api/values.yaml.

    .EXAMPLE
    ./setup-db.ps1
    ./setup-db.ps1 -RootPassword "annat-losenord"
#>

[CmdletBinding()]
param(
    [string]$KubeContext    = "k3d-home",
    [string]$Namespace      = "mysql",
    [string]$Pod            = "mysql-0",   # bitnami/mysql: StatefulSet "mysql"
    [string]$Container      = "mysql",
    [string]$RootPassword   = "SuperSecret123!",   # = $MySQLRootPwd i deploy-infra.ps1
    [string]$Database       = "cqrs",
    [string]$WriterPassword = "writer-pwd",
    [string]$ReaderPassword = "reader-pwd"
)

$ErrorActionPreference = "Stop"

$sql = @"
CREATE DATABASE IF NOT EXISTS ``$Database``;

CREATE USER IF NOT EXISTS 'cqrs_writer'@'%' IDENTIFIED BY '$WriterPassword';
ALTER USER 'cqrs_writer'@'%' IDENTIFIED BY '$WriterPassword';
GRANT ALL PRIVILEGES ON ``$Database``.* TO 'cqrs_writer'@'%';

CREATE USER IF NOT EXISTS 'cqrs_reader'@'%' IDENTIFIED BY '$ReaderPassword';
ALTER USER 'cqrs_reader'@'%' IDENTIFIED BY '$ReaderPassword';
GRANT SELECT ON ``$Database``.* TO 'cqrs_reader'@'%';

FLUSH PRIVILEGES;
"@

Write-Host "==> Väntar på att $Namespace/$Pod är redo..." -ForegroundColor Cyan
kubectl --context $KubeContext wait pod/$Pod -n $Namespace --for=condition=Ready --timeout=180s
if ($LASTEXITCODE -ne 0) { throw "$Namespace/$Pod blev inte Ready. Har deploy-infra.ps1 körts?" }

Write-Host "==> Skapar databasen '$Database' och användarna cqrs_writer / cqrs_reader..." -ForegroundColor Cyan
# MYSQL_PWD istället för -p<lösenord>: slipper varningen om lösenord på kommandoraden.
$sql | kubectl --context $KubeContext exec -i $Pod -n $Namespace -c $Container -- `
    env MYSQL_PWD=$RootPassword mysql -uroot
if ($LASTEXITCODE -ne 0) { throw "SQL-skriptet misslyckades." }

Write-Host "==> Kontroll - rättigheter:" -ForegroundColor Cyan
"SHOW GRANTS FOR 'cqrs_writer'@'%'; SHOW GRANTS FOR 'cqrs_reader'@'%';" |
    kubectl --context $KubeContext exec -i $Pod -n $Namespace -c $Container -- `
        env MYSQL_PWD=$RootPassword mysql -uroot --table

Write-Host "==> Klart! Workern skapar tabellen Persons själv när den startar." -ForegroundColor Green
