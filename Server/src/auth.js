// CloSim master server — client API-key gating.
//
// The directory API is game-client-only. Every request (except /healthz) must carry a valid client
// credential in the `X-Api-Key` header — the exact header A2's MasterServerClient sends. Requests
// without a recognized key are rejected 401. This is what keeps the directory from being a casually
// browsable public web endpoint (it is HTTP under the hood, but not open).
//
// Keys are configured out-of-band (env CLIENT_API_KEYS / AWS Secrets Manager) and are BLANK in the
// committed template. Multiple keys are supported so keys can be rotated or issued per build.

import { timingSafeEqual } from 'node:crypto';

const API_KEY_HEADER = 'x-api-key'; // Express lowercases header names

// Constant-time membership check to avoid leaking key length/prefix via timing.
function keyMatches(provided, accepted) {
  const p = Buffer.from(provided, 'utf8');
  let matched = false;
  for (const key of accepted) {
    const k = Buffer.from(key, 'utf8');
    // timingSafeEqual requires equal lengths; guard, but still do the compare when lengths match.
    if (k.length === p.length && timingSafeEqual(k, p)) {
      matched = true;
    }
  }
  return matched;
}

/**
 * Build the auth middleware.
 * @param {{ clientApiKeys: string[], allowInsecureNoAuth: boolean }} cfg
 */
export function makeApiKeyAuth(cfg) {
  const accepted = cfg.clientApiKeys;
  const openForDev = accepted.length === 0 && cfg.allowInsecureNoAuth;

  return function apiKeyAuth(req, res, next) {
    if (openForDev) {
      // Local-dev escape hatch only (validateConfig refuses this combo in real deploys).
      return next();
    }

    const provided = req.get(API_KEY_HEADER);
    if (!provided || !keyMatches(provided, accepted)) {
      res.status(401).json({ ok: false, error: 'unauthorized: missing or invalid X-Api-Key' });
      return;
    }
    return next();
  };
}
