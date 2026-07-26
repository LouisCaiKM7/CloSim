// CloSim master server — process entrypoint.
//
// Boots the Express directory API, starts the TTL reaper, and wires graceful shutdown. All config
// comes from the environment (see config.js / .env.example) — nothing is hardcoded.

import { loadConfig, validateConfig } from './config.js';
import { RoomStore } from './roomStore.js';
import { createApp, SERVICE_NAME } from './app.js';

function log(cfg, level, msg, extra) {
  const order = { error: 0, warn: 1, info: 2, debug: 3 };
  if ((order[level] ?? 2) > (order[cfg.logLevel] ?? 2)) return;
  const line = `[${new Date().toISOString()}] [${level}] ${msg}`;
  // eslint-disable-next-line no-console
  console[level === 'debug' ? 'log' : level](extra ? `${line} ${JSON.stringify(extra)}` : line);
}

function main() {
  const config = loadConfig();
  try {
    validateConfig(config);
  } catch (e) {
    // eslint-disable-next-line no-console
    console.error(`[fatal] ${e.message}`);
    process.exit(1);
    return;
  }

  const store = new RoomStore({
    roomTtlSeconds: config.roomTtlSeconds,
    maxRooms: config.maxRooms,
  });

  const app = createApp({ store, config });

  const server = app.listen(config.port, config.bindHost, () => {
    log(config, 'info', `${SERVICE_NAME} listening`, {
      host: config.bindHost,
      port: config.port,
      authGated: config.clientApiKeys.length > 0,
      insecureDevMode: config.clientApiKeys.length === 0 && config.allowInsecureNoAuth,
      roomTtlSeconds: config.roomTtlSeconds,
      heartbeatSeconds: config.heartbeatSeconds,
    });
    if (config.clientApiKeys.length === 0 && config.allowInsecureNoAuth) {
      log(config, 'warn', 'Running WITHOUT auth (ALLOW_INSECURE_NO_AUTH) — local dev only.');
    }
  });

  // TTL reaper: periodically drop rooms whose heartbeats have lapsed.
  const sweepMs = Math.max(1, config.sweepIntervalSeconds) * 1000;
  const reaper = setInterval(() => {
    const reaped = store.sweep();
    if (reaped > 0) log(config, 'debug', `reaped ${reaped} expired room(s)`, { live: store.size });
  }, sweepMs);
  if (typeof reaper.unref === 'function') reaper.unref();

  function shutdown(signal) {
    log(config, 'info', `received ${signal}, shutting down`);
    clearInterval(reaper);
    server.close(() => process.exit(0));
    // Force-exit if connections linger.
    setTimeout(() => process.exit(0), 5000).unref();
  }
  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGINT', () => shutdown('SIGINT'));
}

main();
