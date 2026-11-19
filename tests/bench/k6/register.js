import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL, JSON_HEADERS, PASSWORD, EMAIL_DOMAIN } from './lib/config.js';

const VUS = Number(__ENV.VUS || 25);
const DURATION = __ENV.DURATION || '2m';
const THINK_TIME = Number(__ENV.THINK_TIME || 0);

export const options = {
  scenarios: {
    register: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '20s', target: VUS },
        { duration: DURATION, target: VUS },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    // 503 capacity_exhausted is a legitimate response when the Argon2 gate is
    // saturated; keep the gate loose and read the status breakdown instead.
    http_req_failed: ['rate<0.05'],
    'http_req_duration{endpoint:register}': ['p(95)<2000'],
  },
};

export default function () {
  // Each iteration must use a fresh email: registering an existing address
  // returns 409 and skips password hashing.
  const runId = __ENV.RUN_ID || String(Date.now());
  const email = `bench-reg-${runId}-${__VU}-${__ITER}@${EMAIL_DOMAIN}`;

  const res = http.post(
    `${BASE_URL}/api/v1/auth/register`,
    JSON.stringify({ email, password: PASSWORD, name: 'Bench User' }),
    { headers: JSON_HEADERS, tags: { endpoint: 'register' }, redirects: 0 },
  );

  check(res, {
    'register status 202': (r) => r.status === 202,
  });

  if (THINK_TIME > 0) sleep(THINK_TIME);
}
