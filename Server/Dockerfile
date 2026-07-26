# CloSim master server — container image.
# Build context is the Server/ directory:  docker build -t <ecr_image_uri> .
#
# Minimal Node runtime, production deps only, runs as non-root, listens on $PORT (default 8080).
# Config is injected at runtime via env vars (see .env.example / deploy IaC) — NOTHING baked in.

FROM node:20-alpine

# App lives here; node user (uid 1000) ships with the image.
WORKDIR /app

# Install production dependencies first (better layer caching).
COPY package.json package-lock.json* ./
RUN npm ci --omit=dev || npm install --omit=dev

# App source.
COPY src ./src

ENV NODE_ENV=production
# Default port; override via env at deploy time.
ENV PORT=8080
EXPOSE 8080

USER node

# Lightweight container healthcheck hitting the JSON /healthz route.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD node -e "fetch('http://127.0.0.1:'+(process.env.PORT||8080)+'/healthz').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

CMD ["node", "src/server.js"]
