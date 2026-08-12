Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
    Seeds deterministic local demo accounts into PostgreSQL for manual API demos.

.DESCRIPTION
    Local development only. Uses docker compose exec against the project postgres service.
    Idempotent: existing demo accounts are upserted with a fresh balance and version reset.
    Never run against production.
#>

$RootDir = Split-Path -Parent $PSScriptRoot
Set-Location $RootDir

$EnvFile = if ($env:ENV_FILE) { $env:ENV_FILE } else { ".env" }
$ComposeProject = if ($env:COMPOSE_PROJECT) { $env:COMPOSE_PROJECT } else { $null }

if (-not (Test-Path $EnvFile)) {
    throw "Missing $EnvFile. Copy .env.example to .env and set local development values first."
}

$ComposeArgs = @("compose")
if ($ComposeProject) {
    $ComposeArgs += @("-p", $ComposeProject)
}
$ComposeArgs += @("--env-file", $EnvFile)

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    & docker @ComposeArgs @Args
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed: $($Args -join ' ')"
    }
}

$postgresContainer = (Invoke-Compose ps -q postgres | Select-Object -First 1)
if (-not $postgresContainer) {
    throw "PostgreSQL container is not running. Start Compose first: docker compose up --build -d"
}

$seedSql = @'
INSERT INTO account_balance.accounts (id, currency, available_balance, reserved_balance, status, version)
VALUES
    ('11111111-1111-1111-1111-111111111111'::uuid, 'GBP', 10000.0000, 0.0000, 'Active', 1),
    ('22222222-2222-2222-2222-222222222222'::uuid, 'GBP', 10000.0000, 0.0000, 'Active', 1),
    ('33333333-3333-3333-3333-333333333333'::uuid, 'GBP', 10000.0000, 0.0000, 'Active', 1)
ON CONFLICT (id) DO UPDATE
SET available_balance = EXCLUDED.available_balance,
    reserved_balance = 0.0000,
    status = 'Active',
    version = account_balance.accounts.version + 1;
'@

docker exec -i $postgresContainer psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 -c $seedSql

Write-Host "Seeded demo accounts:"
Write-Host "  Source (customer demo):      11111111-1111-1111-1111-111111111111"
Write-Host "  Destination:                 22222222-2222-2222-2222-222222222222"
Write-Host "  Other customer (ownership):  33333333-3333-3333-3333-333333333333"
