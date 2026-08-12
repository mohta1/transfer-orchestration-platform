Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
Set-Location $RootDir

$ComposeProject = if ($env:COMPOSE_PROJECT) { $env:COMPOSE_PROJECT } else { "transfer-orchestration-runtime-verify" }
$EnvFile = if ($env:ENV_FILE) { $env:ENV_FILE } else { ".env.runtime-verify" }
$MarkerTable = "__runtime_volume_marker"
$TimeoutSeconds = if ($env:TIMEOUT_SECONDS) { [int]$env:TIMEOUT_SECONDS } else { 180 }

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ComposeArgs)
    & docker compose -p $ComposeProject @ComposeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed: $($ComposeArgs -join ' ')"
    }
}

function Get-HttpStatusCode {
    param([string]$Url)

    if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        return (curl.exe -s -o NUL -w '%{http_code}' $Url)
    }

    return (Invoke-WebRequest -Uri $Url -SkipHttpErrorCheck -UseBasicParsing).StatusCode.ToString()
}

function Wait-ForHttpStatus {
    param(
        [string]$Url,
        [string]$ExpectedStatus,
        [string]$Label
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $status = Get-HttpStatusCode -Url $Url
            if ($status -eq $ExpectedStatus) {
                Write-Host "$Label is ready ($Url -> HTTP $status)"
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 2
    }

    Invoke-Compose ps
    Invoke-Compose logs --no-color api postgres migrate
    throw "Timed out waiting for $Label at $Url (expected HTTP $ExpectedStatus)."
}

function Wait-ForMigrateComplete {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $migrateId = (Invoke-Compose ps -a -q migrate | Select-Object -First 1)
        if ($migrateId) {
            $state = docker inspect --format='{{.State.Status}}' $migrateId
            if ($state -eq "exited") {
                $exitCode = docker inspect --format='{{.State.ExitCode}}' $migrateId
                if ($exitCode -eq "0") {
                    Write-Host "migrate completed successfully"
                    return
                }

                Invoke-Compose logs --no-color migrate
                throw "migrate exited with code $exitCode"
            }
        }

        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for migrate to complete."
}

function Wait-ForComposeHealth {
    param([string]$Service)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $containerId = (Invoke-Compose ps -q $Service | Select-Object -First 1)
        if ($containerId) {
            $health = docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $containerId
            if ($health -eq "healthy") {
                Write-Host "$Service is healthy"
                return
            }
        }

        Start-Sleep -Seconds 2
    }

    Invoke-Compose ps
    Invoke-Compose logs --no-color $Service
    throw "Timed out waiting for $Service to become healthy."
}

try {
    @"
POSTGRES_PASSWORD=local-runtime-verify-password
JWT_SIGNING_KEY=LOCAL_RUNTIME_VERIFY_32_BYTE_SIGNING_KEY
"@ | Set-Content -Path $EnvFile -NoNewline

    Write-Host "Building and starting Compose runtime..."
    Invoke-Compose --env-file $EnvFile up --build --detach

    Wait-ForComposeHealth postgres
    Wait-ForMigrateComplete
    Wait-ForHttpStatus "http://127.0.0.1:8080/health/live" "200" "API liveness"
    Wait-ForHttpStatus "http://127.0.0.1:8080/health/ready" "200" "API readiness"

    $liveBody = if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        curl.exe -fsS "http://127.0.0.1:8080/health/live"
    }
    else {
        (Invoke-WebRequest -Uri "http://127.0.0.1:8080/health/live" -UseBasicParsing).Content
    }
    $readyBody = if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        curl.exe -fsS "http://127.0.0.1:8080/health/ready"
    }
    else {
        (Invoke-WebRequest -Uri "http://127.0.0.1:8080/health/ready" -UseBasicParsing).Content
    }
    Write-Host "Liveness response: $liveBody"
    Write-Host "Readiness response: $readyBody"

    $markerValue = "runtime-marker-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())-$([Guid]::NewGuid().ToString('N'))"
    $postgresContainer = (Invoke-Compose ps -q postgres | Select-Object -First 1)

    docker exec $postgresContainer psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 -c `
        "CREATE TABLE IF NOT EXISTS public.$MarkerTable (marker_id text PRIMARY KEY, created_at_utc timestamptz NOT NULL DEFAULT now());"
    docker exec $postgresContainer psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 -c `
        "INSERT INTO public.$MarkerTable (marker_id) VALUES ('$markerValue');"

    $beforeCount = (docker exec $postgresContainer psql -U transfer_app -d transfer_orchestration -At -c `
        "SELECT COUNT(*) FROM public.$MarkerTable WHERE marker_id = '$markerValue';").Trim()
    if ($beforeCount -ne "1") {
        throw "Expected marker row before restart, found count=$beforeCount"
    }

    Write-Host "Stopping API and PostgreSQL without deleting the named volume..."
    Invoke-Compose stop api postgres

    Write-Host "Recreating API and PostgreSQL containers..."
    Invoke-Compose --env-file $EnvFile up --detach --force-recreate postgres migrate api

    Wait-ForComposeHealth postgres
    Wait-ForMigrateComplete
    Wait-ForHttpStatus "http://127.0.0.1:8080/health/live" "200" "API liveness after restart"
    Wait-ForHttpStatus "http://127.0.0.1:8080/health/ready" "200" "API readiness after restart"

    $postgresContainer = (Invoke-Compose ps -q postgres | Select-Object -First 1)
    $afterCount = (docker exec $postgresContainer psql -U transfer_app -d transfer_orchestration -At -c `
        "SELECT COUNT(*) FROM public.$MarkerTable WHERE marker_id = '$markerValue';").Trim()
    if ($afterCount -ne "1") {
        throw "Expected marker row after restart, found count=$afterCount"
    }

    Write-Host "Persistent volume marker survived restart: $markerValue"

    Write-Host "Verifying readiness failure when PostgreSQL is unavailable..."
    Invoke-Compose stop postgres

    $readinessStatus = $null
    $readinessDeadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $readinessDeadline) {
        $readinessStatus = Get-HttpStatusCode -Url "http://127.0.0.1:8080/health/ready"
        if ($readinessStatus -eq "503") {
            Write-Host "Readiness reported unavailable while PostgreSQL was stopped (HTTP 503)"
            break
        }

        Start-Sleep -Seconds 2
    }

    if ($readinessStatus -ne "503") {
        throw "Expected readiness HTTP 503 while PostgreSQL was stopped, got $readinessStatus"
    }

    $liveStatus = Get-HttpStatusCode -Url "http://127.0.0.1:8080/health/live"
    if ($liveStatus -ne "200") {
        throw "Expected liveness to remain HTTP 200 while PostgreSQL was stopped, got $liveStatus"
    }

    Write-Host "Restarting PostgreSQL and waiting for readiness recovery..."
    Invoke-Compose --env-file $EnvFile up --detach postgres
    Wait-ForComposeHealth postgres
    Wait-ForHttpStatus "http://127.0.0.1:8080/health/ready" "200" "API readiness after PostgreSQL recovery"

    docker exec $postgresContainer psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 -c `
        "DELETE FROM public.$MarkerTable WHERE marker_id = '$markerValue';"

    Write-Host "Compose runtime verification succeeded."
    Invoke-Compose ps
}
finally {
    try {
        Invoke-Compose down -v --remove-orphans | Out-Null
    }
    catch {
    }

    if (Test-Path $EnvFile) {
        Remove-Item $EnvFile -Force
    }
}
