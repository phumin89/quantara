FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
COPY server/package*.json server/
COPY webview-ui/package*.json webview-ui/
RUN npm ci
COPY . .
RUN npm run compile

FROM node:20-alpine
WORKDIR /app
COPY --from=build /app/package*.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
EXPOSE 4242
CMD ["node", "dist/cli.js", "--host", "0.0.0.0", "--port", "4242"]
