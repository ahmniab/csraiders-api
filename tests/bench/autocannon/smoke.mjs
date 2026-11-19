// Short autocannon smoke test. k6 is the primary harness; this is here for a
// quick sanity check and for people who prefer autocannon's output.
//
// autocannon has no cookie jar and builds one RequestIterator per connection,
// so a fixed `requests` array is replayed by every connection. Instead we use a
// single request template with `setupRequest`, which is invoked for every
// request in the process (autocannon's default single worker), letting us hand
// out a unique user, email, or refresh token to every request.
//
// Refresh tokens are single-use. The refresh scenario mints a wallet up front
// and runs in `amount` mode, so each token is used once. That makes it a short
// smoke, not a sustained refresh benchmark; use k6 for that.

import fs from 'node:fs';
import autocannon from 'autocannon';

const BASE_URL = process.env.BASE_URL || 'http://api:8080';
const ORIGIN = process.env.ORIGIN || 'http://bench.local';

const PASSWORD = process.env.BENCH_PASSWORD || 'Bench-Passw0rd!';
const EMAIL_PREFIX = process.env.BENCH_EMAIL_PREFIX || 'bench';
const EMAIL_DOMAIN = process.env.BENCH_EMAIL_DOMAIN || 'bench.local';
const USER_COUNT = Number(process.env.BENCH_USER_COUNT || 20000);

const SCENARIO = process.env.SCENARIO || 'login';
const CONNECTIONS = Number(process.env.CONNECTIONS || 25);
const DURATION = Number(process.env.DURATION || 10);
const WALLET = Number(process.env.WALLET || 1000);
const RESULTS_DIR = process.env.RESULTS_DIR || './results';
const RUN_ID = process.env.RUN_ID || String(Date.now());

const jsonHeaders = { 'content-type': 'application/json' };

const emailFor = (i) => `${EMAIL_PREFIX}+${String(i).padStart(7, '0')}@${EMAIL_DOMAIN}`;

function readRefreshCookie(response) {
  const setCookies = response.headers.getSetCookie
    ? response.headers.getSetCookie()
    : [response.headers.get('set-cookie')].filter(Boolean);

  const header = setCookies.find((c) => c && c.startsWith('__Host-csraiders_rt='));
  if (!header) return null;

  const pair = header.split(';', 1)[0];
  return pair.slice(pair.indexOf('=') + 1);
}

async function mintRefreshWallet(size) {
  const wallet = new Array(size);
  let next = 0;

  async function worker() {
    while (true) {
      const i = next++;
      if (i >= size) return;

      const dfp = `bench-dfp-${String(i).padStart(7, '0')}`;

      const response = await fetch(`${BASE_URL}/api/v1/auth/login`, {
        method: 'POST',
        headers: { ...jsonHeaders, cookie: `csraiders_dfp=${dfp}` },
        body: JSON.stringify({ email: emailFor(i), password: PASSWORD }),
        redirect: 'manual',
      });

      if (response.status !== 200) {
        throw new Error(`wallet login ${i} failed: ${response.status} ${await response.text()}`);
      }

      const refreshToken = readRefreshCookie(response);
      if (!refreshToken) throw new Error(`wallet login ${i} returned no refresh cookie`);

      wallet[i] = { refreshToken, dfp };
    }
  }

  // Login is Argon2-bound on the server, so 8 concurrent minters are enough to
  // overlap HTTP/DB without turning setup into the actual stress test.
  await Promise.all(Array.from({ length: Math.min(8, size) }, () => worker()));
  return wallet;
}

let loginIndex = 0;
let registerIndex = 0;
let refreshIndex = 0;
let refreshWallet = [];

const loginTemplate = {
  method: 'POST',
  path: '/api/v1/auth/login',
  headers: jsonHeaders,
  setupRequest: (request) => {
    const index = loginIndex++;
    return {
      ...request,
      body: JSON.stringify({ email: emailFor(index % USER_COUNT), password: PASSWORD }),
    };
  },
};

const registerTemplate = {
  method: 'POST',
  path: '/api/v1/auth/register',
  headers: jsonHeaders,
  setupRequest: (request) => {
    const index = registerIndex++;
    return {
      ...request,
      body: JSON.stringify({
        email: `bench-reg-${RUN_ID}-${index}@${EMAIL_DOMAIN}`,
        password: PASSWORD,
        name: 'Bench User',
      }),
    };
  },
};

const refreshTemplate = {
  method: 'POST',
  path: '/api/v1/auth/refresh',
  headers: jsonHeaders,
  setupRequest: (request) => {
    // setupRequest is also called once while the iterator is built, before any
    // request is sent. Minting WALLET + 1 tokens covers that wasted call.
    const entry = refreshWallet[refreshIndex++];
    if (!entry) throw new Error('refresh wallet exhausted; amount must match WALLET');

    return {
      ...request,
      headers: {
        ...jsonHeaders,
        origin: ORIGIN,
        'x-requested-with': 'csraiders',
        cookie: `__Host-csraiders_rt=${entry.refreshToken}; csraiders_dfp=${entry.dfp}`,
      },
      body: '{}',
    };
  },
};

if (SCENARIO === 'refresh') {
  console.log(`[autocannon] minting ${WALLET + 1} refresh tokens...`);
  refreshWallet = await mintRefreshWallet(WALLET + 1);
}

const template =
  SCENARIO === 'refresh' ? refreshTemplate : SCENARIO === 'register' ? registerTemplate : loginTemplate;

if (SCENARIO !== 'login' && SCENARIO !== 'refresh' && SCENARIO !== 'register') {
  throw new Error(`unknown SCENARIO '${SCENARIO}' (login|refresh|register)`);
}

const instance = autocannon(
  {
    url: BASE_URL,
    connections: CONNECTIONS,
    timeout: 10,
    requests: [template],
    headers: { connection: 'keep-alive' },
    ...(SCENARIO === 'refresh' ? { amount: WALLET } : { duration: DURATION }),
  },
  (err, result) => {
    if (err) {
      console.error(err);
      process.exitCode = 1;
      return;
    }

    const summary = {
      scenario: SCENARIO,
      connections: CONNECTIONS,
      durationSeconds: SCENARIO === 'refresh' ? null : DURATION,
      amount: SCENARIO === 'refresh' ? WALLET : null,
      requests: result.requests,
      latency: result.latency,
      throughput: result.throughput,
      statusCodes: result.statusCodeStats,
      errors: result.errors,
      timeouts: result.timeouts,
    };

    console.log(JSON.stringify(summary, null, 2));

    fs.mkdirSync(RESULTS_DIR, { recursive: true });
    fs.writeFileSync(
      `${RESULTS_DIR}/autocannon-${SCENARIO}.json`,
      JSON.stringify(summary, null, 2),
    );
  },
);

autocannon.track(instance, { renderProgressBar: true });
