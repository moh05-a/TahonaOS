#!/usr/bin/env bash
# Backs up the TahonaOS database (and uploaded files, when the API container is running) from a Docker Compose deployment.
#
#   scripts/backup.sh [output-dir]
#
# Reads POSTGRES_USER and POSTGRES_DB from the environment or from .env in the repository root.
# Environment: DB_CONTAINER (default tahona-db), API_CONTAINER (default tahona-api), RETENTION_DAYS (default 30).
# Schedule it daily (cron) and copy the output directory off the server: a backup on the same disk is not a backup.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/backups}"
DB_CONTAINER="${DB_CONTAINER:-tahona-db}"
API_CONTAINER="${API_CONTAINER:-tahona-api}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"

env_value() { [ -f "$ROOT/.env" ] && grep -E "^$1=" "$ROOT/.env" | tail -n 1 | cut -d= -f2- || true; }
POSTGRES_USER="${POSTGRES_USER:-$(env_value POSTGRES_USER)}"; POSTGRES_USER="${POSTGRES_USER:-tahona}"
POSTGRES_DB="${POSTGRES_DB:-$(env_value POSTGRES_DB)}"; POSTGRES_DB="${POSTGRES_DB:-tahona}"

STAMP="$(date -u +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"
DUMP="$OUT/tahona-db-$STAMP.dump"

echo "Backing up database '$POSTGRES_DB' from container '$DB_CONTAINER'…"
docker exec "$DB_CONTAINER" pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner --no-privileges > "$DUMP"

# A dump that cannot be listed is useless: fail now, not on the day it is needed.
docker exec -i "$DB_CONTAINER" pg_restore --list < "$DUMP" > /dev/null
echo "  database: $DUMP ($(du -h "$DUMP" | cut -f1))"

if docker ps --format '{{.Names}}' | grep -qx "$API_CONTAINER"; then
  FILES="$OUT/tahona-files-$STAMP.tar.gz"
  MSYS_NO_PATHCONV=1 docker exec "$API_CONTAINER" tar -czf - -C /data storage > "$FILES"
  echo "  files:    $FILES ($(du -h "$FILES" | cut -f1))"
else
  echo "  files:    skipped (container '$API_CONTAINER' is not running)"
fi

( cd "$OUT" && sha256sum tahona-*-"$STAMP".* > "tahona-$STAMP.sha256" )

# Retention: remove backups older than RETENTION_DAYS.
find "$OUT" -maxdepth 1 -type f -name 'tahona-*' -mtime "+$RETENTION_DAYS" -print -delete | sed 's/^/  removed old backup: /' || true
echo "Done. Test a restore regularly with scripts/restore.sh."
