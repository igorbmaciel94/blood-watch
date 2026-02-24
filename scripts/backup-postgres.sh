#!/usr/bin/env bash
set -euo pipefail

COMPOSE_DIR="${COMPOSE_DIR:-/opt/bloodwatch/compose}"
COMPOSE_FILE="${COMPOSE_FILE:-${COMPOSE_DIR}/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-${COMPOSE_DIR}/.env}"
BACKUP_DIR="${BACKUP_DIR:-/opt/bloodwatch/backups}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"

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

mkdir -p "${BACKUP_DIR}"
timestamp="$(date -u +"%Y%m%dT%H%M%SZ")"
backup_file="${BACKUP_DIR}/bloodwatch_${timestamp}.sql.gz"

compose() {
  docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" "$@"
}

echo "Creating backup ${backup_file}..."
compose exec -T postgres sh -lc 'PGPASSWORD="${POSTGRES_PASSWORD}" pg_dump -U "${POSTGRES_USER}" "${POSTGRES_DB}"' \
  | gzip -c > "${backup_file}"

gzip -t "${backup_file}"
echo "Backup created and verified: ${backup_file}"

echo "Applying retention policy (${RETENTION_DAYS} days)..."
find "${BACKUP_DIR}" -type f -name "bloodwatch_*.sql.gz" -mtime +"$((RETENTION_DAYS - 1))" -print -delete
