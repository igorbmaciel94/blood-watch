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

compose_cmd=(docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}")

wait_for_api() {
  local retries=20
  local wait_seconds=3
  local i

  for ((i = 1; i <= retries; i++)); do
    if "${compose_cmd[@]}" exec -T api curl -fsS "http://localhost:8080/health/ready" >/dev/null; then
      echo "API is healthy with Copilot disabled."
      return 0
    fi

    echo "Waiting for API health (${i}/${retries})..."
    sleep "${wait_seconds}"
  done

  echo "API health verification failed." >&2
  return 1
}

echo "Disabling Copilot on API..."
BloodWatch__Copilot__Enabled=false "${compose_cmd[@]}" up -d --force-recreate api

echo "Stopping Ollama profile..."
"${compose_cmd[@]}" --profile copilot stop ollama || true

wait_for_api
echo "Copilot OFF completed."
