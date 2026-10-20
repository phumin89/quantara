FROM node:24-bookworm-slim AS build
WORKDIR /src
ENV NEXT_TELEMETRY_DISABLED=1
COPY package.json package-lock.json ./
COPY apps/web/package.json ./apps/web/package.json
COPY services/ai-worker/package.json ./services/ai-worker/package.json
RUN npm ci
COPY apps/web ./apps/web
COPY vendor/pixel-agents ./vendor/pixel-agents
COPY tools/build-pixel-office.mjs ./tools/build-pixel-office.mjs
RUN node tools/build-pixel-office.mjs
RUN npm exec --workspace @quantara/web -- next build

FROM node:24-bookworm-slim
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 HOSTNAME=0.0.0.0 PORT=3000
COPY --from=build /src/apps/web/.next/standalone ./
COPY --from=build /src/apps/web/.next/static ./apps/web/.next/static
COPY --from=build /src/apps/web/public ./apps/web/public
USER node
EXPOSE 3000
COPY docker/http-healthcheck.mjs ./docker/http-healthcheck.mjs
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD node /app/docker/http-healthcheck.mjs
CMD ["node", "/app/apps/web/server.js"]
