import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, ORIGIN, PASSWORD, USER_COUNT, emailFor } from './lib/config.js';

const VUS = Number(__ENV.VUS || 50);
const DURATION = __ENV.DURATION || '2m';
const SESSIONS = Number(__ENV.REFRESH_SESSIONS || Math.max(VUS, 100));

export const options = {
  setupTimeout: '10m',
  scenarios: {
    refresh: {
      executor: 'constant-vus',
      vus: VUS,
      duration: DURATION,
      gracefulStop: '10s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
    'http_req_duration{endpoint:refresh}': ['p(95)<500'],
  },
};

function refreshCookie(res) {
  const list = res.cookies['__Host-csraiders_rt'] || res.cookies['csraiders_rt'];
  return list && list.length > 0 ? list[0].value : null;
}

// Mint one session per session slot. Each VU then owns exactly one session and
// rotates its refresh token, which is what the real client does.
export function setup() {
  if (VUS > SESSIONS) {
    throw new Error(`VUS (${VUS}) must be <= REFRESH_SESSIONS (${SESSIONS})`);
  }
  if (SESSIONS > USER_COUNT) {
    throw new Error(`REFRESH_SESSIONS (${SESSIONS}) must be <= BENCH_USER_COUNT (${USER_COUNT})`);
  }

  const wallet = [];

  for (let i = 0; i < SESSIONS; i++) {
    const dfp = `bench-dfp-${String(i).padStart(7, '0')}`;
    http.cookieJar().clear(BASE_URL);

    const res = http.post(
      `${BASE_URL}/api/v1/auth/login`,
      JSON.stringify({ email: emailFor(i), password: PASSWORD }),
      {
        headers: {
          'Content-Type': 'application/json',
          'Cookie': `csraiders_dfp=${dfp}`,
        },
        tags: { endpoint: 'refresh-setup' },
        redirects: 0,
      },
    );

    if (res.status !== 200) {
      throw new Error(`setup login ${i} failed: ${res.status} ${res.body}`);
    }

    const rt = refreshCookie(res);
    if (!rt) throw new Error(`setup login ${i} did not return a refresh cookie`);

    wallet.push({ rt, dfp });
  }

  return wallet;
}

let state;

export default function (wallet) {
  if (!state) {
    const seed = wallet[(__VU - 1) % wallet.length];
    state = { rt: seed.rt, dfp: seed.dfp };
    // We manage the Cookie header manually so the Secure attribute never matters.
    http.cookieJar().clear(BASE_URL);
  }

  const res = http.post(`${BASE_URL}/api/v1/auth/refresh`, '{}', {
    headers: {
      'Content-Type': 'application/json',
      'Origin': ORIGIN,
      'X-Requested-With': 'csraiders',
      'Cookie': `__Host-csraiders_rt=${state.rt}; csraiders_dfp=${state.dfp}`,
    },
    tags: { endpoint: 'refresh' },
    redirects: 0,
  });

  if (res.status === 200) {
    const next = refreshCookie(res);
    if (next) state.rt = next;
  }

  check(res, {
    'refresh status 200': (r) => r.status === 200,
    'refresh returns access token': (r) => !!r.json('accessToken'),
  });
}
