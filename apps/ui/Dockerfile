# syntax=docker/dockerfile:1
# Build context is apps/ui, not the monorepo root: the UI shares no code with the .NET projects.
#   docker build -t storywalker-ui \
#     --build-arg NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=pk_... apps/ui

ARG NODE_VERSION=24

# ---- dependencies ----
FROM node:${NODE_VERSION}-alpine AS deps
WORKDIR /app

# Only the manifests first: this layer is cached until a dependency changes, so source-only edits
# skip the install.
COPY package.json package-lock.json ./
RUN npm ci

# ---- build ----
FROM node:${NODE_VERSION}-alpine AS build
WORKDIR /app

# NEXT_PUBLIC_* values are inlined into the client bundle by `next build`, so they are build
# arguments: setting them on the running container is too late for the browser. Both are public by
# design — the Clerk publishable key, and the Application Insights ingestion key (ADR-0023), which
# can send telemetry but read none. Left empty, browser telemetry is simply not built in.
ARG NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=""
ARG NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING=""
ARG NEXT_PUBLIC_CLERK_SIGN_IN_URL=/sign-in
ARG NEXT_PUBLIC_CLERK_SIGN_UP_URL=/sign-up

ENV NEXT_TELEMETRY_DISABLED=1

COPY --from=deps /app/node_modules ./node_modules
COPY . .
# public/ holds no files yet, and git does not keep an empty folder, so a fresh clone has none to
# copy into the runtime stage.
RUN mkdir -p public && npm run build

# ---- runtime ----
FROM node:${NODE_VERSION}-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    # The standalone server binds to HOSTNAME; the container's own hostname would make it
    # unreachable through the published port.
    HOSTNAME=0.0.0.0

# The standalone output (next.config.ts) is a server.js with only the traced node_modules. Static
# assets are not part of it and are copied next to it, where server.js serves them from.
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/public ./public

# The node image's unprivileged user. Runtime settings — API_URL, CLERK_SECRET_KEY,
# APPLICATIONINSIGHTS_CONNECTION_STRING — come from the environment (.env.example).
USER node
EXPOSE 3000
CMD ["node", "server.js"]
