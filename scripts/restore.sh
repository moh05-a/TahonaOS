#!/usr/bin/env bash
# Restores a TahonaOS database backup into a SEPARATE database and reports row counts, so a backup can be
# verified without touching live data.
#
#   scripts/restore.sh <backup.dump> [target-database]        (default target: tahona_restore_check)
#   scripts/restore.sh <backup.dump> --compare <database>     also compares row counts with an existing database
#
# The target database must not exist: this script never overwrites one. To put a restored copy into service,
# stop the API, point ConnectionStrings__Default at the restored database (or rename it), and start the API again.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DUMP="${1:?Usage: scripts/restore.sh <backup.dump> [target-database] [--compare <database>]}"
shift
TARGET="tahona_restore_check"
COMPARE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --compare) COMPARE="${2:?--compare needs a database name}"; shift 2 ;;
    *) TARGET="$1"; shift ;;
  esac
done

DB_CONTAINER="${DB_CONTAINER:-tahona-db}"
env_value() { [ -f "$ROOT/.env" ] && grep -E "^$1=" "$ROOT/.env" | tail -n 1 | cut -d= -f2- || true; }
POSTGRES_USER="${POSTGRES_USER:-$(env_value POSTGRES_USER)}"; POSTGRES_USER="${POSTGRES_USER:-tahona}"

[ -f "$DUMP" ] || { echo "Backup file not found: $DUMP" >&2; exit 1; }
case "$TARGET" in *[!a-zA-Z0-9_]*) echo "Target database name may only contain letters, digits and underscores." >&2; exit 1 ;; esac

psql() { docker exec -i "$DB_CONTAINER" psql -U "$POSTGRES_USER" -v ON_ERROR_STOP=1 "$@"; }

if [ "$(psql -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$TARGET'")" = "1" ]; then
  echo "Database '$TARGET' already exists. Choose another name or drop it first; this script never overwrites a database." >&2
  exit 1
fi

echo "Restoring $DUMP into new database '$TARGET'…"
psql -d postgres -c "CREATE DATABASE \"$TARGET\"" > /dev/null
docker exec -i "$DB_CONTAINER" pg_restore -U "$POSTGRES_USER" -d "$TARGET" --no-owner --no-privileges --exit-on-error < "$DUMP"

TABLES='"Clinics" "Users" "Patients" "Appointments" "TreatmentPlans" "TreatmentRecords" "Invoices" "Payments" "ClinicalNotes" "Messages" "AuditLogs"'
counts() {
  local query=""
  for t in $TABLES; do query+="SELECT '$t' AS t, count(*) FROM $t UNION ALL "; done
  psql -d "$1" -tAF '|' -c "${query% UNION ALL } ORDER BY 1"
}

echo "Row counts in '$TARGET':"
RESTORED="$(counts "$TARGET")"
echo "$RESTORED" | sed 's/|/: /; s/^/  /'

if [ -n "$COMPARE" ]; then
  if [ "$RESTORED" = "$(counts "$COMPARE")" ]; then
    echo "Row counts match '$COMPARE'."
  else
    echo "Row counts DIFFER from '$COMPARE' (expected if '$COMPARE' changed since the backup was taken):" >&2
    diff <(echo "$RESTORED") <(counts "$COMPARE") >&2 || true
    exit 2
  fi
fi
echo "Restore verified. Drop the check database when done:  docker exec $DB_CONTAINER dropdb -U $POSTGRES_USER $TARGET"
