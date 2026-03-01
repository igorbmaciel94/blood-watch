#!/usr/bin/env bash
set -euo pipefail

COMPOSE_DIR="${COMPOSE_DIR:-/opt/bloodwatch/compose}"
COMPOSE_FILE="${COMPOSE_FILE:-${COMPOSE_DIR}/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-${COMPOSE_DIR}/.env}"

if [[ ! -f "${COMPOSE_FILE}" ]]; then
  echo "Compose file not found: ${COMPOSE_FILE}" >&2
  exit 1
fi

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Environment file not found: ${ENV_FILE}" >&2
  exit 1
fi

upsert_env_var() {
  local key="$1"
  local value="$2"
  local tmp_file

  tmp_file="$(mktemp)"

  awk -v key="${key}" -v value="${value}" '
    BEGIN { updated = 0 }
    index($0, key "=") == 1 {
      print key "=" value
      updated = 1
      next
    }
    { print }
    END {
      if (updated == 0) {
        print key "=" value
      }
    }
  ' "${ENV_FILE}" > "${tmp_file}"

  mv "${tmp_file}" "${ENV_FILE}"
}

resolve_docker_sock_gid() {
  local socket_path="/var/run/docker.sock"

  if [[ ! -S "${socket_path}" ]]; then
    return 1
  fi

  if stat -c '%g' "${socket_path}" >/dev/null 2>&1; then
    stat -c '%g' "${socket_path}"
    return 0
  fi

  if stat -f '%g' "${socket_path}" >/dev/null 2>&1; then
    stat -f '%g' "${socket_path}"
    return 0
  fi

  return 1
}

if [[ $# -gt 0 ]]; then
  export BLOODWATCH_IMAGE_TAG="$1"
  echo "Deploying BLOODWATCH_IMAGE_TAG=${BLOODWATCH_IMAGE_TAG}"
  upsert_env_var "BLOODWATCH_IMAGE_TAG" "${BLOODWATCH_IMAGE_TAG}"
  echo "Pinned BLOODWATCH_IMAGE_TAG=${BLOODWATCH_IMAGE_TAG} in ${ENV_FILE}"
fi

if [[ -n "${BLOODWATCH_IMAGE_TAG:-}" && -z "${BloodWatch__Build__Version:-}" ]]; then
  export BloodWatch__Build__Version="${BLOODWATCH_IMAGE_TAG}"
fi

if [[ -z "${BloodWatch__Build__Commit:-}" ]]; then
  export BloodWatch__Build__Commit="${RELEASE_SHA:-unknown}"
fi

if [[ -z "${BloodWatch__Build__Date:-}" ]]; then
  export BloodWatch__Build__Date="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
fi

if docker_gid="$(resolve_docker_sock_gid)"; then
  export HOST_DOCKER_GID="${docker_gid}"
  upsert_env_var "HOST_DOCKER_GID" "${HOST_DOCKER_GID}"
  echo "Detected HOST_DOCKER_GID=${HOST_DOCKER_GID} from /var/run/docker.sock"
else
  echo "Warning: unable to detect /var/run/docker.sock group. Using HOST_DOCKER_GID from env or compose default."
fi

compose() {
  docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" "$@"
}

wait_for_health() {
  local service="$1"
  local endpoint="$2"
  local retries=20
  local wait_seconds=5
  local i

  for ((i = 1; i <= retries; i++)); do
    if compose exec -T "${service}" curl -fsS "${endpoint}" >/dev/null; then
      echo "Service ${service} is healthy at ${endpoint}"
      return 0
    fi

    echo "Waiting for ${service} health (${i}/${retries})..."
    sleep "${wait_seconds}"
  done

  echo "Health verification failed for ${service} at ${endpoint}" >&2
  return 1
}

echo "Pulling newest images..."
compose pull api worker

echo "Ensuring Postgres is running..."
compose up -d postgres

echo "Preparing on-demand Copilot containers (not started)..."
compose --profile copilot create ollama ollama-model-init || true

echo "Running one-shot migrator..."
compose run --rm migrator

echo "Starting application services..."
compose up -d api worker caddy

wait_for_health api "http://localhost:8080/health/ready"
wait_for_health worker "http://localhost:8081/health/ready"

echo "Deployment completed successfully."
