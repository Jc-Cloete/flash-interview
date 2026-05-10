#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"

if [[ -f "${repo_root}/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${repo_root}/.env"
  set +a
fi

export INITIAL_SUPER_ADMIN_ENABLED="${INITIAL_SUPER_ADMIN_ENABLED:-true}"
export INITIAL_SUPER_ADMIN_EMAIL="${INITIAL_SUPER_ADMIN_EMAIL:-admin@example.test}"
export INITIAL_SUPER_ADMIN_PASSWORD="${INITIAL_SUPER_ADMIN_PASSWORD:-Admin_password123!}"

cd "${repo_root}"

printf 'Starting Flash Interview dev stack.\n'
printf 'Local admin: %s / <password set>\n' "${INITIAL_SUPER_ADMIN_EMAIL}"

if [[ -n "${GOOGLE_CLIENT_ID:-}" && -n "${GOOGLE_CLIENT_SECRET:-}" ]]; then
  printf 'Google sign-in: enabled\n'
else
  printf 'Google sign-in: disabled. Set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET to show the button.\n'
fi

exec docker compose -f docker-compose.dev.yml up --build
