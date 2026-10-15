FROM node:24-bookworm-slim AS node-runtime

FROM node:24-bookworm-slim AS worker-deps
WORKDIR /src
COPY package.json package-lock.json ./
COPY apps/web/package.json ./apps/web/package.json
COPY services/ai-worker/package.json ./services/ai-worker/package.json
RUN npm ci --workspace @quantara/ai-worker --include-workspace-root=false --omit=dev --ignore-scripts

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Quantara.sln ./
COPY Directory.*.props ./
COPY Directory.*.targets ./
COPY .config ./.config
COPY services ./services
COPY tools ./tools
RUN dotnet restore services/team/Team.Api/Team.Api.csproj
RUN dotnet publish services/team/Team.Api/Team.Api.csproj -c Release --no-restore \
    -p:QuantaraAutoFormat=false -p:UseAppHost=true -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP_UID=1654
RUN apt-get update && apt-get install --no-install-recommends -y curl && rm -rf /var/lib/apt/lists/*
COPY --from=node-runtime /usr/local/bin/node /usr/local/bin/node
WORKDIR /app
COPY --from=worker-deps /src/node_modules ./node_modules
COPY services/ai-worker ./services/ai-worker
COPY --from=build /out ./
RUN chmod +x /app/Team.Api
ENV ASPNETCORE_URLS=http://+:5052
USER ${APP_UID}
EXPOSE 5052
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD curl --fail --silent --show-error --max-time 2 http://127.0.0.1:5052/health || exit 1
ENTRYPOINT ["/app/Team.Api"]
