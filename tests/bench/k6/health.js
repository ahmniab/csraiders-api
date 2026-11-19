// Pure baseline scenario: GET /health with no auth, no DB, no Argon2.
// Use this to find the Kestrel/routing ceiling of the 2-vCPU container.
// If health saturates at ~X req/s while login tops out at ~40, the delta
// is explained by Argon2 — not network, not Kestrel, not the DB.

import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL } from './lib/config.js';

const VUS = Number(__ENV.VUS || 100);
const DURATION = __ENV.DURATION || '1m';

export const options = {
  scenarios: {
    health: {
      executor: 'constant-vus',
      vus: VUS,
      duration: DURATION,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    'http_req_duration{endpoint:health}': ['p(99)<50'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/health`, {
    tags: { endpoint: 'health' },
  });

  check(res, {
    'health status 200': (r) => r.status === 200,
    'health body ok': (r) => r.json('status') === 'ok',
  });
}
