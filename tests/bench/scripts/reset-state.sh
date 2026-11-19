#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
COMPOSE_FILE="$ROOT/tests/docker-compose-benchmark.yml"

cd "$ROOT"

api_id="$(docker compose -f "$COMPOSE_FILE" ps -q api || true)"
if [ -z "$api_id" ]; then
  echo "api container is not running; start the stack first." >&2
  exit 1
fi

echo "Truncating per-run state (users, roles, and app_settings are kept)..."
docker compose -f "$COMPOSE_FILE" exec -T postgres psql \
  -U bench -d csraiders_bench -v ON_ERROR_STOP=1 -c \
  "TRUNCATE TABLE sessions, login_attempts, pending_registrations, password_reset_tokens, security_events, dead_letter_emails CASCADE;"

echo "Restarting the API to clear the in-memory distributed cache..."
docker compose -f "$COMPOSE_FILE" restart api >/dev/null

for _ in $(seq 1 60); do
  status="$(docker inspect -f '{{.State.Health.Status}}' "$api_id" 2>/dev/null || echo unknown)"
  if [ "$status" = "healthy" ]; then
    echo "API is healthy."
    exit 0
  fi
  sleep 1
done

echo "API did not become healthy in 60s." >&2
exit 1
