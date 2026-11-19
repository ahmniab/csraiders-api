import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL, JSON_HEADERS, PASSWORD, USER_COUNT, emailFor } from './lib/config.js';

const VUS = Number(__ENV.VUS || 50);
const DURATION = __ENV.DURATION || '2m';
const THINK_TIME = Number(__ENV.THINK_TIME || 0);

// Each virtual user owns a block of user indexes. Keep iterations per VU below
// USER_BLOCK so two VUs never log in as the same seeded user at the same time.
const USER_BLOCK = Number(__ENV.USER_BLOCK || 5000);

export const options = {
  scenarios: {
    login: {
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
    http_req_failed: ['rate<0.02'],
    'http_req_duration{endpoint:login}': ['p(95)<2000'],
  },
};

export default function () {
  const index = ((__VU - 1) * USER_BLOCK + __ITER) % USER_COUNT;

  const res = http.post(
    `${BASE_URL}/api/v1/auth/login`,
    JSON.stringify({ email: emailFor(index), password: PASSWORD }),
    { headers: JSON_HEADERS, tags: { endpoint: 'login' } },
  );

  check(res, {
    'login status 200': (r) => r.status === 200,
    'login returns access token': (r) => !!r.json('accessToken'),
  });

  if (THINK_TIME > 0) sleep(THINK_TIME);
}
