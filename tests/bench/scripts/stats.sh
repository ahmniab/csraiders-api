#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
COMPOSE_FILE="$ROOT/tests/docker-compose-benchmark.yml"

cd "$ROOT"

ids="$(docker compose -f "$COMPOSE_FILE" ps -q postgres api)"
if [ -z "$ids" ]; then
  echo "postgres/api are not running; start the stack first." >&2
  exit 1
fi

# Streams until interrupted. Redirect to a file to keep a CSV-ish record:
#   tests/bench/scripts/stats.sh > tests/bench/results/docker-stats.tsv
# CPU% is relative to one core: a fully saturated 2-core container shows ~200%.
exec docker stats --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}' $ids
