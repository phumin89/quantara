FROM node:24-bookworm-slim AS build
WORKDIR /src
ENV NEXT_TELEMETRY_DISABLED=1
COPY package.json package-lock.json ./
COPY apps/web/package.json ./apps/web/package.json
COPY services/ai-worker/package.json ./services/ai-worker/package.json
RUN npm ci
COPY apps/web ./apps/web
RUN npm exec --workspace @quantara/web -- next build

FROM node:24-bookworm-slim
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 HOSTNAME=0.0.0.0 PORT=3000
COPY --from=build /src/apps/web/.next/standalone ./
COPY --from=build /src/apps/web/.next/static ./apps/web/.next/static
USER node
EXPOSE 3000
COPY docker/http-healthcheck.mjs ./docker/http-healthcheck.mjs
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD node /app/docker/http-healthcheck.mjs
CMD ["node", "/app/apps/web/server.js"]
