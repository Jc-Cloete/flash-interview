#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
compose_file="${repo_root}/docker-compose.yml"

read_dotenv_value() {
  local key="$1"

  if [[ ! -f "${repo_root}/.env" ]]; then
    return 1
  fi

  awk -v key="$key" '
    function trim(value) {
      gsub(/^[ \t]+|[ \t]+$/, "", value)
      return value
    }

    /^[ \t]*(#|$)/ { next }
    {
      line = $0
      sub(/\r$/, "", line)
      sub(/^[ \t]*export[ \t]+/, "", line)
      separator = index(line, "=")
      if (separator == 0) {
        next
      }

      name = trim(substr(line, 1, separator - 1))
      if (name != key) {
        next
      }

      value = trim(substr(line, separator + 1))
      if (value ~ /^".*"$/ || value ~ /^\047.*\047$/) {
        value = substr(value, 2, length(value) - 2)
      }
      dotenv_value = value
      found = 1
    }

    END {
      if (!found) {
        exit 1
      }
      print dotenv_value
    }
  ' "${repo_root}/.env"
}

set_default_from_shell_dotenv_or_fallback() {
  local key="$1"
  local fallback="$2"
  local current_value="${!key:-}"
  local dotenv_value

  if [[ -n "$current_value" ]]; then
    export "$key=$current_value"
    return
  fi

  if dotenv_value="$(read_dotenv_value "$key")"; then
    export "$key=$dotenv_value"
    return
  fi

  export "$key=$fallback"
}

cleanup() {
  local exit_code=$?

  if [[ "$exit_code" -ne 0 ]]; then
    printf '\nSmoke script did not complete. Recent API logs:\n'
    docker compose -f "${compose_file}" logs --tail=100 api || true
    printf '\nStopping smoke-test stack:\n'
    docker compose -f "${compose_file}" down || true
  fi

  exit "$exit_code"
}

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

set_default_from_shell_dotenv_or_fallback MSSQL_SA_PASSWORD "Your_strong_password123"
set_default_from_shell_dotenv_or_fallback FLASHINTERVIEW_ADMIN_API_KEY "local-smoke-admin-key"
export DATABASE_APPLY_MIGRATIONS_ON_STARTUP=true
export DATABASE_SEED_ON_STARTUP=true

cd "${repo_root}"

docker compose -f "${compose_file}" up --build -d

printf 'Waiting for API readiness'
for _ in {1..60}; do
  if curl -fsS http://localhost:8080/readyz >/dev/null; then
    printf '\nAPI ready: http://localhost:8080\n'
    break
  fi
  printf '.'
  sleep 2
done

if ! curl -fsS http://localhost:8080/readyz >/dev/null; then
  printf '\nAPI did not become ready before timeout.\n'
  exit 1
fi

printf 'Waiting for MVC readiness'
for _ in {1..60}; do
  if curl -fsS http://localhost:8081/ >/dev/null; then
    printf '\nMVC ready: http://localhost:8081\n'
    printf 'Smoke stack left running. Stop it with: docker compose -f %q down\n' "${compose_file}"
    exit 0
  fi
  printf '.'
  sleep 2
done

printf '\nMVC did not become ready before timeout.\n'
exit 1
