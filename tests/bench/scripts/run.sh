#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
COMPOSE_FILE="$ROOT/tests/docker-compose-benchmark.yml"
RESULTS_DIR="$ROOT/tests/bench/results"

mkdir -p "$RESULTS_DIR"
cd "$ROOT"

compose() {
  docker compose -f "$COMPOSE_FILE" "$@"
}

usage() {
  cat <<'EOF'
Usage: tests/bench/scripts/run.sh <command>

  up                     Build images and start postgres + seed + api
  build                  Build images only
  down                   Stop everything and delete the postgres volume
  stop                   Stop everything, keep the postgres volume
  logs                   Follow API logs
  k6 <scenario> [args]   Run k6 health|login|refresh|register
  autocannon <scenario>  Run the autocannon smoke (login|refresh|register)
  all                    Run k6 login, refresh, then register
  reset                  Truncate run state and restart the API
  stats                  Stream docker stats for postgres + api
  psql                   Open psql against the benchmark database

Examples:
  tests/bench/scripts/run.sh up
  VUS=100 DURATION=3m tests/bench/scripts/run.sh k6 login
  REFRESH_SESSIONS=250 VUS=200 tests/bench/scripts/run.sh k6 refresh
  tests/bench/scripts/run.sh autocannon register
EOF
}

command="${1:-help}"
if [ "$#" -gt 0 ]; then shift; fi

case "$command" in
  up)
    compose up -d --build
    ;;
  build)
    compose build
    ;;
  down)
    compose --profile load down -v
    ;;
  stop)
    compose --profile load down
    ;;
  logs)
    compose logs -f --tail=100 api
    ;;
  k6)
    scenario="${1:-login}"
    if [ "$#" -gt 0 ]; then shift; fi
    overrides=(-e "RUN_ID=$(date +%s%N)")
    for variable in VUS DURATION THINK_TIME USER_BLOCK REFRESH_SESSIONS; do
      if [ -n "${!variable:-}" ]; then overrides+=(-e "${variable}=${!variable}"); fi
    done
    compose --profile load run --rm "${overrides[@]}" \
      k6 run --summary-export="/results/${scenario}-summary.json" "/scripts/${scenario}.js" "$@"
    ;;
  autocannon)
    scenario="${1:-login}"
    if [ "$#" -gt 0 ]; then shift; fi
    overrides=(-e "SCENARIO=${scenario}" -e "RUN_ID=$(date +%s%N)")
    for variable in CONNECTIONS DURATION WALLET; do
      if [ -n "${!variable:-}" ]; then overrides+=(-e "${variable}=${!variable}"); fi
    done
    compose --profile load run --rm "${overrides[@]}" autocannon "$@"
    ;;
  all)
    for scenario in health login refresh register; do
      echo
      echo "=== k6: $scenario ==="
      compose --profile load run --rm \
        -e RUN_ID="$(date +%s%N)" \
        k6 run --summary-export="/results/${scenario}-summary.json" "/scripts/${scenario}.js"
    done
    ;;
  reset)
    "$SCRIPT_DIR/reset-state.sh"
    ;;
  stats)
    "$SCRIPT_DIR/stats.sh"
    ;;
  psql)
    compose exec postgres psql -U bench -d csraiders_bench
    ;;
  *)
    usage
    exit 1
    ;;
esac
