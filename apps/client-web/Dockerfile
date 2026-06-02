# Web client container (app §A4 / §A14, core §10.3). Multi-stage: build the Vite bundle from
# the monorepo, then serve the static assets with nginx. Regtest/play-money web app — plays
# heads-up Hold'em vs a bot entirely in the browser (the real engine compiled to JS).
#
# Build context is the repo ROOT:
#   docker build -f apps/client-web/Dockerfile -t bsv-poker-web .
#   docker run --rm -p 8080:80 bsv-poker-web   # → http://localhost:8080

FROM node:24-alpine AS build
RUN corepack enable && corepack prepare pnpm@9.0.0 --activate
WORKDIR /app
COPY . .
RUN pnpm install --frozen-lockfile
RUN pnpm --filter @bsv-poker/client-web build

FROM nginx:1.27-alpine
COPY --from=build /app/apps/client-web/dist /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://127.0.0.1/ >/dev/null 2>&1 || exit 1
