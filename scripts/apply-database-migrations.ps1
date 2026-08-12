Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
Set-Location $RootDir

if (-not $env:ConnectionStrings__Database -and -not $env:TEST_DATABASE_CONNECTION_STRING) {
    throw "ConnectionStrings__Database or TEST_DATABASE_CONNECTION_STRING must be set."
}

if (-not $env:ConnectionStrings__Database) {
    $env:ConnectionStrings__Database = $env:TEST_DATABASE_CONNECTION_STRING
}

dotnet tool restore

$StartupProject = "src/TransferOrchestration.Api/TransferOrchestration.Api.csproj"
$Migrations = @(
    @{ Project = "src/Modules/AccountBalance/TransferOrchestration.AccountBalance.csproj"; Context = "AccountBalanceDbContext" },
    @{ Project = "src/Modules/TransferManagement/TransferOrchestration.TransferManagement.csproj"; Context = "TransferManagementDbContext" },
    @{ Project = "src/Modules/Notification/TransferOrchestration.Notification.csproj"; Context = "NotificationDbContext" },
    @{ Project = "src/Modules/AuditOperations/TransferOrchestration.AuditOperations.csproj"; Context = "AuditOperationsDbContext" }
)

foreach ($migration in $Migrations) {
    Write-Host "Applying migrations for $($migration.Context)..."
    dotnet ef database update `
        --startup-project $StartupProject `
        --project $migration.Project `
        --context $migration.Context
}

Write-Host "All module migrations applied successfully."
