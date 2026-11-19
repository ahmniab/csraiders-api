// Shared configuration for the k6 scenarios.
// The seeder creates emails as `<prefix>+<7-digit index>@<domain>`.

export const BASE_URL = __ENV.BASE_URL || 'http://api:8080';
export const ORIGIN = __ENV.ORIGIN || 'http://bench.local';

export const PASSWORD = __ENV.BENCH_PASSWORD || 'Bench-Passw0rd!';
export const EMAIL_PREFIX = __ENV.BENCH_EMAIL_PREFIX || 'bench';
export const EMAIL_DOMAIN = __ENV.BENCH_EMAIL_DOMAIN || 'bench.local';
export const USER_COUNT = Number(__ENV.BENCH_USER_COUNT || 20000);

export const JSON_HEADERS = { 'Content-Type': 'application/json' };

export function emailFor(index) {
  return `${EMAIL_PREFIX}+${String(index).padStart(7, '0')}@${EMAIL_DOMAIN}`;
}
