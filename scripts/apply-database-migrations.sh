set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ -z "${ConnectionStrings__Database:-}" && -z "${TEST_DATABASE_CONNECTION_STRING:-}" ]]; then
  echo "ConnectionStrings__Database or TEST_DATABASE_CONNECTION_STRING must be set." >&2
  exit 1
fi

export ConnectionStrings__Database="${ConnectionStrings__Database:-${TEST_DATABASE_CONNECTION_STRING}}"
export TEST_DATABASE_CONNECTION_STRING="${TEST_DATABASE_CONNECTION_STRING:-${ConnectionStrings__Database}}"

dotnet tool restore

STARTUP_PROJECT="src/TransferOrchestration.Api/TransferOrchestration.Api.csproj"

apply_migration() {
  local project="$1"
  local context="$2"
  echo "Applying migrations for ${context}..."
  dotnet ef database update \
    --startup-project "$STARTUP_PROJECT" \
    --project "$project" \
    --context "$context"
}

apply_migration "src/Modules/AccountBalance/TransferOrchestration.AccountBalance.csproj" "AccountBalanceDbContext"
apply_migration "src/Modules/TransferManagement/TransferOrchestration.TransferManagement.csproj" "TransferManagementDbContext"
apply_migration "src/Modules/Notification/TransferOrchestration.Notification.csproj" "NotificationDbContext"
apply_migration "src/Modules/AuditOperations/TransferOrchestration.AuditOperations.csproj" "AuditOperationsDbContext"

echo "All module migrations applied successfully."
