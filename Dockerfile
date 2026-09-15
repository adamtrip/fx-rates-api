# syntax=docker/dockerfile:1
# The build stage runs on the builder's own architecture and cross-compiles for TARGETARCH, so a
# multi-platform build on an amd64 CI runner never runs the .NET SDK under emulation. The API publish
# is framework-dependent IL with no apphost, so it is the same for every platform; only the
# migration bundle is architecture-specific.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
# Restore from project and package files only, so editing source does not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY .config/dotnet-tools.json .config/
COPY src/FxRates.Api/FxRates.Api.csproj src/FxRates.Api/
COPY src/FxRates.Application/FxRates.Application.csproj src/FxRates.Application/
COPY src/FxRates.Infrastructure/FxRates.Infrastructure.csproj src/FxRates.Infrastructure/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/FxRates.Api/FxRates.Api.csproj && dotnet tool restore
COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/FxRates.Api/FxRates.Api.csproj -c Release --no-restore -o /out/api /p:UseAppHost=false
# The bundle is a self-contained executable that applies pending migrations. It finds
# RatesDbContextFactory at run time, so it needs only ConnectionStrings__Rates.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    RID="linux-x64"; if [ "$TARGETARCH" = "arm64" ]; then RID="linux-arm64"; fi; \
    dotnet ef migrations bundle --project src/FxRates.Infrastructure --startup-project src/FxRates.Api \
    --configuration Release --target-runtime "$RID" --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
USER root
# curl is only for the container health check; the runtime image does not ship it.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID

FROM runtime-base AS migrator
COPY --from=build --chown=app:app /out/efbundle ./efbundle
ENTRYPOINT ["./efbundle"]

FROM runtime-base AS api
COPY --from=build --chown=app:app /out/api .
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "FxRates.Api.dll"]
