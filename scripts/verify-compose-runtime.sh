#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

COMPOSE_PROJECT="${COMPOSE_PROJECT:-transfer-orchestration-runtime-verify}"
COMPOSE=(docker compose -p "$COMPOSE_PROJECT")
ENV_FILE="${ENV_FILE:-.env.runtime-verify}"
MARKER_TABLE="__runtime_volume_marker"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-180}"

cleanup() {
  "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -f "$ENV_FILE"
}

trap cleanup EXIT

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command not found: $1" >&2
    exit 1
  fi
}

wait_for_http_status() {
  local url="$1"
  local expected_status="$2"
  local label="$3"
  local deadline=$((SECONDS + TIMEOUT_SECONDS))

  while (( SECONDS < deadline )); do
    local status
    status="$(curl -s -o /dev/null -w '%{http_code}' "$url" || true)"
    if [[ "$status" == "$expected_status" ]]; then
      echo "$label is ready ($url -> HTTP $status)"
      return 0
    fi

    sleep 2
  done

  echo "Timed out waiting for $label at $url (expected HTTP $expected_status)." >&2
  "${COMPOSE[@]}" ps || true
  "${COMPOSE[@]}" logs --no-color api postgres migrate || true
  exit 1
}

wait_for_compose_health() {
  local service="$1"
  local deadline=$((SECONDS + TIMEOUT_SECONDS))

  while (( SECONDS < deadline )); do
    local container_id
    container_id="$("${COMPOSE[@]}" ps -q "$service")"
    if [[ -n "$container_id" ]]; then
      local health
      health="$(docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id")"
      if [[ "$health" == "healthy" ]]; then
        echo "$service is healthy"
        return 0
      fi
    fi

    sleep 2
  done

  echo "Timed out waiting for $service to become healthy." >&2
  "${COMPOSE[@]}" ps || true
  "${COMPOSE[@]}" logs --no-color "$service" || true
  exit 1
}

wait_for_migrate() {
  local deadline=$((SECONDS + TIMEOUT_SECONDS))

  while (( SECONDS < deadline )); do
    local migrate_id
    migrate_id="$("${COMPOSE[@]}" ps -a -q migrate | head -n 1)"
    if [[ -n "$migrate_id" ]]; then
      local state exit_code
      state="$(docker inspect --format='{{.State.Status}}' "$migrate_id")"
      if [[ "$state" == "exited" ]]; then
        exit_code="$(docker inspect --format='{{.State.ExitCode}}' "$migrate_id")"
        if [[ "$exit_code" == "0" ]]; then
          echo "migrate completed successfully"
          return 0
        fi

        "${COMPOSE[@]}" logs --no-color migrate || true
        echo "migrate exited with code $exit_code" >&2
        exit 1
      fi
    fi

    sleep 2
  done

  echo "Timed out waiting for migrate to complete." >&2
  exit 1
}

require_command docker
require_command curl

cat > "$ENV_FILE" <<'EOF'
POSTGRES_PASSWORD=local-runtime-verify-password
JWT_SIGNING_KEY=LOCAL_RUNTIME_VERIFY_32_BYTE_SIGNING_KEY
EOF

echo "Building and starting Compose runtime..."
"${COMPOSE[@]}" --env-file "$ENV_FILE" up --build --detach

wait_for_compose_health postgres
wait_for_migrate
wait_for_http_status "http://127.0.0.1:8080/health/live" "200" "API liveness"
wait_for_http_status "http://127.0.0.1:8080/health/ready" "200" "API readiness"

LIVE_BODY="$(curl -fsS http://127.0.0.1:8080/health/live)"
READY_BODY="$(curl -fsS http://127.0.0.1:8080/health/ready)"
echo "Liveness response: $LIVE_BODY"
echo "Readiness response: $READY_BODY"

MARKER_VALUE="runtime-marker-$(date +%s)-$RANDOM"
POSTGRES_CONTAINER="$("${COMPOSE[@]}" ps -q postgres)"

docker exec "$POSTGRES_CONTAINER" psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 -c \
  "CREATE TABLE IF NOT EXISTS public.${MARKER_TABLE} (marker_id text PRIMARY KEY, created_at_utc timestamptz NOT NULL DEFAULT now());"

docker exec "$POSTGRES_CONTAINER" psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 -c \
  "INSERT INTO public.${MARKER_TABLE} (marker_id) VALUES ('${MARKER_VALUE}');"

BEFORE_COUNT="$(docker exec "$POSTGRES_CONTAINER" psql -U transfer_app -d transfer_orchestration -At -c \
  "SELECT COUNT(*) FROM public.${MARKER_TABLE} WHERE marker_id = '${MARKER_VALUE}';")"

if [[ "$BEFORE_COUNT" != "1" ]]; then
  echo "Expected marker row before restart, found count=$BEFORE_COUNT" >&2
  exit 1
fi

echo "Stopping API and PostgreSQL without deleting the named volume..."
"${COMPOSE[@]}" stop api postgres

echo "Recreating API and PostgreSQL containers..."
"${COMPOSE[@]}" --env-file "$ENV_FILE" up --detach --force-recreate postgres migrate api

wait_for_compose_health postgres
wait_for_migrate
wait_for_http_status "http://127.0.0.1:8080/health/live" "200" "API liveness after restart"
wait_for_http_status "http://127.0.0.1:8080/health/ready" "200" "API readiness after restart"

POSTGRES_CONTAINER="$("${COMPOSE[@]}" ps -q postgres)"
AFTER_COUNT="$(docker exec "$POSTGRES_CONTAINER" psql -U transfer_app -d transfer_orchestration -At -c \
  "SELECT COUNT(*) FROM public.${MARKER_TABLE} WHERE marker_id = '${MARKER_VALUE}';")"

if [[ "$AFTER_COUNT" != "1" ]]; then
  echo "Expected marker row after restart, found count=$AFTER_COUNT" >&2
  exit 1
fi

echo "Persistent volume marker survived restart: $MARKER_VALUE"

echo "Verifying readiness failure when PostgreSQL is unavailable..."
"${COMPOSE[@]}" stop postgres

READINESS_DEADLINE=$((SECONDS + 90))
while (( SECONDS < READINESS_DEADLINE )); do
  READINESS_STATUS="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/health/ready || true)"
  if [[ "$READINESS_STATUS" == "503" ]]; then
    echo "Readiness reported unavailable while PostgreSQL was stopped (HTTP 503)"
    break
  fi
  sleep 2
done

if [[ "${READINESS_STATUS:-}" != "503" ]]; then
  echo "Expected readiness HTTP 503 while PostgreSQL was stopped, got ${READINESS_STATUS:-none}" >&2
  exit 1
fi

LIVE_STATUS="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/health/live || true)"
if [[ "$LIVE_STATUS" != "200" ]]; then
  echo "Expected liveness to remain HTTP 200 while PostgreSQL was stopped, got $LIVE_STATUS" >&2
  exit 1
fi

echo "Restarting PostgreSQL and waiting for readiness recovery..."
"${COMPOSE[@]}" --env-file "$ENV_FILE" up --detach postgres
wait_for_compose_health postgres
wait_for_http_status "http://127.0.0.1:8080/health/ready" "200" "API readiness after PostgreSQL recovery"

docker exec "$POSTGRES_CONTAINER" psql -U transfer_app -d transfer_orchestration -v ON_ERROR_STOP=1 -c \
  "DELETE FROM public.${MARKER_TABLE} WHERE marker_id = '${MARKER_VALUE}';"

echo "Compose runtime verification succeeded."
"${COMPOSE[@]}" ps
