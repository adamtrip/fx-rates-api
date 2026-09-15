#!/usr/bin/env bash
# Runs the same checks as CI, plus the deployment-file checks listed in docs/verification.md.
# Docker must be running for the integration tests.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet tool restore
dotnet restore FxRates.slnx
dotnet format FxRates.slnx --no-restore --verify-no-changes
dotnet build FxRates.slnx --no-restore --configuration Release
dotnet test FxRates.slnx --no-build --configuration Release
dotnet ef migrations has-pending-model-changes \
  --project src/FxRates.Infrastructure --startup-project src/FxRates.Api --configuration Release --no-build
docker compose config --quiet
bash -n deploy/deploy.sh
if command -v actionlint >/dev/null 2>&1; then
  actionlint -config-file .github/actionlint.yaml .github/workflows/ci-cd.yml
else
  echo 'actionlint is not installed; workflow lint skipped.'
fi
echo 'All checks passed.'
