#!/usr/bin/env bash
set -euo pipefail

DEFAULT_PROD_DIR="/opt/bloodwatch/compose"

resolve_compose_file() {
  if [[ -n "${COMPOSE_FILE:-}" ]]; then
    echo "${COMPOSE_FILE}"
    return
  fi

  if [[ -f "${DEFAULT_PROD_DIR}/docker-compose.prod.yml" ]]; then
    echo "${DEFAULT_PROD_DIR}/docker-compose.prod.yml"
    return
  fi

  if [[ -f "./docker-compose.yml" ]]; then
    echo "./docker-compose.yml"
    return
  fi

  echo "Unable to find compose file. Set COMPOSE_FILE explicitly." >&2
  exit 1
}

COMPOSE_FILE="$(resolve_compose_file)"
ENV_FILE="${ENV_FILE:-$(dirname "${COMPOSE_FILE}")/.env}"

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

: "${BloodWatch__Copilot__AdminApiKey:?BloodWatch__Copilot__AdminApiKey must be set in ${ENV_FILE}}"
: "${OLLAMA__BASE_URL:?OLLAMA__BASE_URL must be set in ${ENV_FILE}}"
: "${OLLAMA__MODEL:?OLLAMA__MODEL must be set in ${ENV_FILE}}"

compose_cmd=(docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}")

wait_for_api() {
  local retries=20
  local wait_seconds=3
  local i

  for ((i = 1; i <= retries; i++)); do
    if "${compose_cmd[@]}" exec -T api curl -fsS "http://localhost:8080/health/ready" >/dev/null; then
      echo "API is healthy and Copilot-enabled."
      return 0
    fi

    echo "Waiting for API health (${i}/${retries})..."
    sleep "${wait_seconds}"
  done

  echo "API health verification failed." >&2
  return 1
}

echo "Starting Ollama profile..."
"${compose_cmd[@]}" --profile copilot up -d ollama ollama-model-init

echo "Enabling Copilot on API..."
BloodWatch__Copilot__Enabled=true "${compose_cmd[@]}" up -d --force-recreate api

wait_for_api
echo "Copilot ON completed."
