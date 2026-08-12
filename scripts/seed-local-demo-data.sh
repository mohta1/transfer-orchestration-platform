#!/usr/bin/env bash
set -euo pipefail

# Local development only. Seeds deterministic demo accounts via docker compose exec.
# Never run against production.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

ENV_FILE="${ENV_FILE:-.env}"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-}"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Missing $ENV_FILE. Copy .env.example to .env and set local development values first." >&2
  exit 1
fi

compose=(docker compose --env-file "$ENV_FILE")
if [[ -n "$COMPOSE_PROJECT" ]]; then
  compose+=(-p "$COMPOSE_PROJECT")
fi

postgres_container="$("${compose[@]}" ps -q postgres | head -n 1)"
if [[ -z "$postgres_container" ]]; then
  echo "PostgreSQL container is not running. Start Compose first: docker compose up --build -d" >&2
  exit 1
fi

docker exec -i "$postgres_container" psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 <<'SQL'
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
SQL

cat <<'EOF'
Seeded demo accounts:
  Source (customer demo):      11111111-1111-1111-1111-111111111111
  Destination:                 22222222-2222-2222-2222-222222222222
  Other customer (ownership):  33333333-3333-3333-3333-333333333333
EOF
