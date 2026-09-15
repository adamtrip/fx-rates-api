#!/usr/bin/env bash
# Called from a checkout of the tested main commit on the dedicated deployment runner.
# Optional: IMAGE_REPOSITORY overrides the registry path (default in deploy/compose.production.yml).
set -euo pipefail
sha="${1:?Pass the tested 40-character commit SHA}"
if [[ ! "$sha" =~ ^[a-f0-9]{40}$ ]]; then
  echo 'Invalid deployment commit SHA.' >&2
  exit 1
fi
state_dir="${FX_DEPLOY_DIR:-$HOME/fx-rates-api-deploy}"
if [[ ! -f "$state_dir/.env" ]]; then
  echo "Configure $state_dir/.env before deploying." >&2
  exit 1
fi
export IMAGE_TAG="$sha"
compose=(docker compose --env-file "$state_dir/.env" -f compose.yml -f deploy/compose.production.yml)
"${compose[@]}" pull postgres rabbitmq migrate api
"${compose[@]}" up -d --no-build --wait postgres rabbitmq
# Migrate before replacing the current API. A failed migration stops the rollout.
"${compose[@]}" run --rm --no-deps migrate
"${compose[@]}" up -d --no-build --no-deps --wait --wait-timeout 120 api
"${compose[@]}" exec -T api curl --fail --silent http://localhost:8080/health/ready
printf '%s\n' "$sha" > "$state_dir/current-sha"
echo 'Deployment healthy. Database migrations are not automatically rolled back.'
