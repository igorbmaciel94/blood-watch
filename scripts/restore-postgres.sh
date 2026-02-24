#!/usr/bin/env bash
set -euo pipefail

COMPOSE_DIR="${COMPOSE_DIR:-/opt/bloodwatch/compose}"
COMPOSE_FILE="${COMPOSE_FILE:-${COMPOSE_DIR}/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-${COMPOSE_DIR}/.env}"

usage() {
  cat <<'EOF'
Usage:
  restore-postgres.sh <backup-file.sql.gz> [--apply]

Behavior:
  - Always validates restore by loading the backup into a temporary database.
  - If --apply is provided, also restores into the production database.
EOF
}

if [[ $# -lt 1 || $# -gt 2 ]]; then
  usage
  exit 1
fi

backup_file="$1"
apply_restore="false"
if [[ $# -eq 2 ]]; then
  if [[ "$2" != "--apply" ]]; then
    usage
    exit 1
  fi
  apply_restore="true"
fi

if [[ ! -f "${backup_file}" ]]; then
  echo "Backup file not found: ${backup_file}" >&2
  exit 1
fi

if [[ ! -f "${COMPOSE_FILE}" ]]; then
  echo "Compose file not found: ${COMPOSE_FILE}" >&2
  exit 1
fi

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Environment file not found: ${ENV_FILE}" >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
source "${ENV_FILE}"
set +a

: "${POSTGRES_DB:?POSTGRES_DB must be set in ${ENV_FILE}}"
: "${POSTGRES_USER:?POSTGRES_USER must be set in ${ENV_FILE}}"

compose() {
  docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" "$@"
}

run_psql() {
  local sql="$1"
  compose exec -T postgres sh -lc "PGPASSWORD=\"\${POSTGRES_PASSWORD}\" psql -v ON_ERROR_STOP=1 -U \"\${POSTGRES_USER}\" -d postgres -c \"$sql\""
}

restore_to_db() {
  local db_name="$1"
  gunzip -c "${backup_file}" \
    | compose exec -T postgres sh -lc "PGPASSWORD=\"\${POSTGRES_PASSWORD}\" psql -v ON_ERROR_STOP=1 -U \"\${POSTGRES_USER}\" -d \"${db_name}\""
}

verify_db() {
  local db_name="$1"
  compose exec -T postgres sh -lc "PGPASSWORD=\"\${POSTGRES_PASSWORD}\" psql -v ON_ERROR_STOP=1 -U \"\${POSTGRES_USER}\" -d \"${db_name}\" -c \"select count(*) as table_count from information_schema.tables where table_schema='public';\""
}

tmp_db="bloodwatch_restore_verify_$(date +%s)"
echo "Creating temporary verification database: ${tmp_db}"
run_psql "DROP DATABASE IF EXISTS \"${tmp_db}\";"
run_psql "CREATE DATABASE \"${tmp_db}\";"

echo "Restoring backup into verification database..."
restore_to_db "${tmp_db}"
echo "Running verification query..."
verify_db "${tmp_db}"

echo "Dropping verification database..."
run_psql "DROP DATABASE IF EXISTS \"${tmp_db}\";"

if [[ "${apply_restore}" != "true" ]]; then
  echo "Verification successful. Production database was not modified."
  exit 0
fi

echo "Applying restore into production database: ${POSTGRES_DB}"
run_psql "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${POSTGRES_DB}' AND pid <> pg_backend_pid();"
run_psql "DROP DATABASE IF EXISTS \"${POSTGRES_DB}\";"
run_psql "CREATE DATABASE \"${POSTGRES_DB}\";"
restore_to_db "${POSTGRES_DB}"
verify_db "${POSTGRES_DB}"

echo "Production restore completed successfully."
