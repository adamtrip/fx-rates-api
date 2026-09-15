# Deployment

The target setup is a public GHCR image, Docker Compose on the existing host, a GitHub self-hosted runner, and the existing Cloudflare Tunnel at `fin-demo.adamtrip.pt`.

The host is an Apple Silicon Mac running Docker Desktop, so the deployed images are the `linux/arm64` variants. The Dockerfile cross-compiles from the CI runner's own architecture, so publishing both `linux/amd64` and `linux/arm64` on a hosted runner does not run the .NET SDK under emulation. The `linux/amd64` image exists for anyone running the stack on an x64 host.

## Pipeline

[The workflow](../.github/workflows/ci-cd.yml) builds and tests pull requests on GitHub-hosted runners. Integration tests start disposable PostgreSQL containers there. Self-hosted deployment runners must not execute pull-request code.

A successful push to `main` publishes application and migration images for `linux/amd64` and `linux/arm64`, tagged with the commit SHA:

```text
ghcr.io/adamtrip/fx-rates-api:<sha>
ghcr.io/adamtrip/fx-rates-api-migrations:<sha>
```

The deployment job runs on the dedicated `fx-rates-prod` runner label and deploys the matching images only after tests and image publication succeed. Commit tags make the deployed revision identifiable. No `latest` tag is required for deployment.

Until the runner exists, the workflow runs a placeholder job on a hosted runner instead. It prints the commit and image names it would deploy and exits successfully, so a push to `main` still completes the pipeline. Set the repository variable `FX_DEPLOY_ENABLED` to `true` to switch to the real job. Do not set it before the runner and the approval settings below are in place.

## Initial host setup

1. Create `adamtrip/fx-rates-api` as a public personal repository and publish the code.
2. Enable the workflow permissions needed to publish packages. Make the GHCR packages public so the deployment host can pull them without registry credentials.
3. Before attaching the runner, set repository **Settings → Actions → General → Fork pull request workflows** to **Require approval for all external contributors**, including returning contributors. Only trusted maintainers may have write access. Review the entire workflow change before approving any external workflow run; reject any PR job targeting the self-hosted runner. Protect `main` against unreviewed changes. A workflow job's `if` condition and runner label do not enforce this boundary because a PR can change the YAML. Verify this approval setting with a test fork PR before enabling the runner. See [GitHub's repository Actions settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository).
4. Create the GitHub `production` environment and restrict deployment to `main`. Register a repository-scoped self-hosted runner on the deployment host with the `fx-rates-prod` label. Run it as a service with Docker Compose access. Keep its registration token out of files and logs.
5. Create a persistent deployment directory, defaulting to `$HOME/fx-rates-api-deploy`. Set `FX_DEPLOY_DIR` if using another location.
6. Place a protected `.env` file in that directory, based on `.env.example`. Set `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, `EXCHANGE_PROVIDER_MODE=AlphaVantage`, and `ALPHA_VANTAGE_API_KEY`. Keep this file outside the runner checkout and limit its filesystem permissions.
7. Choose an unused `API_PORT`, default `5080`, and `RABBITMQ_MANAGEMENT_PORT`, default `15672`. The Compose stack binds both to localhost. Configure the existing tunnel hostname to reach the API port only; the management UI stays local.
8. Set the repository variable `FX_DEPLOY_ENABLED` to `true` once steps 3 to 7 are verified.

If `cloudflared` runs inside a container, its `localhost` is not the Docker host. Use the existing infrastructure's routing convention or a deliberately shared Docker network. Do not expose PostgreSQL through the tunnel.

The host needs Docker, Compose, outbound access for pulling GHCR images and Alpha Vantage calls, and enough disk space for PostgreSQL and images. Docker access gives the runner substantial host privileges. The approval policy depends on maintainer review; it is not runner isolation. If untrusted workflows need to run without this review, keep this deployment runner detached and use an isolated runner or a private deployment repository. Repository settings cannot be installed by the checked-in workflow; runner registration remains blocked until they are configured and checked.

## Deploy a tested revision

The workflow invokes:

```sh
bash deploy/deploy.sh <commit-sha>
```

The script uses the base Compose file and `deploy/compose.production.yml`, with `IMAGE_TAG` selecting both application and migration images and `IMAGE_REPOSITORY` optionally overriding the registry path. Runtime credentials come from the persistent deployment directory, not the image.

Migrations run before the API starts. PostgreSQL and RabbitMQ use persistent named volumes, so replacing an API container does not erase rates or queued messages. Keep the Compose project name and deployment directory stable to keep using those volumes.

After deployment, verify:

```sh
curl --fail https://fin-demo.adamtrip.pt/health/live
curl --fail https://fin-demo.adamtrip.pt/health/ready
curl --fail https://fin-demo.adamtrip.pt/openapi/v1.json
```

Use interactive documentation to check a missing pair once credentials are configured. Repeating the same lookup should return the same stored quote. Provider quota errors are possible and should return a clear error without inserting a row.

## Recovery

Retain the previously deployed commit SHA. Redeploying an earlier image is possible only if it remains compatible with the current database schema. Migrations do not automatically reverse during application rollback. Back up PostgreSQL before a migration that could remove or transform data.

This is a single-host POC with possible short downtime during replacement. Host failure, database backups, automated rollback, and high availability are outside the initial implementation.
