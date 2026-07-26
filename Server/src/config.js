// CloSim master server — runtime configuration.
//
// EVERYTHING is read from the environment. There are NO hardcoded endpoints, keys, or secrets
// in this repo. See .env.example for the blank template the user fills in.
//
// The master server is a backend directory API whose only client is CloSim.exe. It is gated by a
// client API key (X-Api-Key). While CLIENT_API_KEYS is blank, the server refuses to start unless
// ALLOW_INSECURE_NO_AUTH=true is explicitly set (local dev only), so a real deployment can never
// accidentally run ungated.

function parseIntEnv(name, fallback) {
  const raw = process.env[name];
  if (raw === undefined || raw === '') return fallback;
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) ? n : fallback;
}

function parseBoolEnv(name, fallback) {
  const raw = process.env[name];
  if (raw === undefined || raw === '') return fallback;
  return /^(1|true|yes|on)$/i.test(raw.trim());
}

// Accepted client API keys: comma-separated. Blank => none configured.
function parseKeys(raw) {
  if (!raw) return [];
  return raw
    .split(',')
    .map((k) => k.trim())
    .filter((k) => k.length > 0);
}

export function loadConfig(env = process.env) {
  const clientApiKeys = parseKeys(env.CLIENT_API_KEYS);
  const allowInsecureNoAuth = parseBoolEnv('ALLOW_INSECURE_NO_AUTH', false);

  return {
    // Bind — BLANK/defaults; the user supplies real host/port via env or IaC.
    port: parseIntEnv('PORT', 8080),
    bindHost: env.BIND_HOST && env.BIND_HOST !== '' ? env.BIND_HOST : '0.0.0.0',

    // Auth gating — the client credential. Blank by default (user provides).
    clientApiKeys,
    allowInsecureNoAuth,

    // TTL model — must stay consistent with the game client's cadence.
    // Client (MasterServerConfig): HeartbeatSeconds=15, RoomTtlSeconds=45 (~3 missed beats).
    roomTtlSeconds: parseIntEnv('ROOM_TTL_SECONDS', 45),
    heartbeatSeconds: parseIntEnv('HEARTBEAT_SECONDS', 15),
    // How often the reaper sweeps expired rooms.
    sweepIntervalSeconds: parseIntEnv('SWEEP_INTERVAL_SECONDS', 10),

    // Safety cap on directory size (in-memory store).
    maxRooms: parseIntEnv('MAX_ROOMS', 5000),

    // Free-form region label for /healthz diagnostics only (blank => "").
    regionLabel: env.REGION_LABEL && env.REGION_LABEL !== '' ? env.REGION_LABEL : '',

    logLevel: env.LOG_LEVEL && env.LOG_LEVEL !== '' ? env.LOG_LEVEL : 'info',
  };
}

// Validate config at boot. Throws with an actionable message if the server would run ungated.
export function validateConfig(cfg) {
  if (cfg.clientApiKeys.length === 0 && !cfg.allowInsecureNoAuth) {
    throw new Error(
      'No CLIENT_API_KEYS configured. The directory API must be gated by a client API key.\n' +
        '  - Production/staging: set CLIENT_API_KEYS to one or more comma-separated keys.\n' +
        '  - Local dev only: set ALLOW_INSECURE_NO_AUTH=true to run without a key (never do this in a real deploy).'
    );
  }
  if (cfg.roomTtlSeconds <= cfg.heartbeatSeconds) {
    // Not fatal, but warn: TTL must exceed the heartbeat interval or rooms flap.
    // eslint-disable-next-line no-console
    console.warn(
      `[config] ROOM_TTL_SECONDS (${cfg.roomTtlSeconds}) <= HEARTBEAT_SECONDS (${cfg.heartbeatSeconds}); ` +
        'rooms may expire between heartbeats. Recommended: TTL >= ~3x heartbeat.'
    );
  }
}
