# Benchmark harness

Load-test stack for `CsRaiders.Api` with hard resource ceilings:

- PostgreSQL: **2 vCPU / 2 GiB**
- API: **2 vCPU / 2 GiB**
- Load generator: intentionally unlimited (a CPU-starved generator invalidates results)

k6 is the primary tool. An autocannon smoke is included for quick checks. No
changes were made under `src/`; the harness uses new files plus environment
configuration.

## Quick start

```bash
tests/bench/scripts/run.sh up          # build images, migrate, seed, start API

tests/bench/scripts/run.sh k6 login
tests/bench/scripts/run.sh k6 refresh
tests/bench/scripts/run.sh k6 register

tests/bench/scripts/run.sh autocannon login
```

Run `tests/bench/scripts/run.sh stats` in a second terminal to watch CPU and
memory while a scenario runs. Results are written to `tests/bench/results/`.

Tear down and delete the database volume:

```bash
tests/bench/scripts/run.sh down
```

## Measured baseline (2026-11-19)

One run on the development host, not a guarantee. Use it to sanity-check a new
environment, then repeat on the host you actually care about.

Hardware and configuration:

- Host: 8 vCPU, 31 GiB RAM; Docker 29.9.2, Compose 5.6.0.
- API and PostgreSQL each limited to 2 vCPU / 2 GiB / 2 GiB swap. Verified with
  `docker inspect`: `NanoCpus=2000000000`, `Memory=2147483648`,
  `MemorySwap=2147483648`, `OOMKilled=false`.
- 20,000 seeded users; `BENCH_PASSWORD` hashed with Argon2 `m=8192 KiB, t=2,
  p=1`; PostgreSQL `synchronous_commit=off`.
- k6 2.4.0. The API was restarted (`run.sh reset`) before every scenario.
- Linux `login`/`register` use a 20 s ramp-up, `DURATION` of steady load, then a
  10 s ramp-down; the values below are averages over that whole run. `refresh`
  is constant-VU for `DURATION` and excludes setup.

| Scenario | VUs | Iterations/s | p50 | p95 | Errors | API CPU | PG CPU |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| login | 50 | 39 | 995 ms | 1.90 s | 0 | ~200% | ~27% |
| refresh | 100 | 633 | 119 ms | 179 ms | 0 | ~200% | ~202% |
| refresh | 200 | 581 | 245 ms | 315 ms | 0 | ~199% | ~204% |
| register | 50 | 45 | 884 ms | 1.70 s | 0 | ~209% | ~16% |

(`docker stats` reports slightly over 200% now and then; the cgroup quota is
still 2 cores. Percentages are per container, where 100% is one core.)

Interpretation:

- **login and register are Argon2-bound.** The API sits at its 2-core quota while
  PostgreSQL stays under 30%. Capacity is roughly 40–50 operations/s for this
  Argon2 cost; latency grows to seconds because excess requests queue on the
  hashing semaphore.
- **refresh is CPU-bound on both containers.** At 100 VUs it reaches ~633
  iterations/s with p95 under 200 ms. Raising to 200 VUs does not increase
  throughput (581/s) and roughly doubles latency, so ~600 refresh/s is the
  ceiling of this 2+2 vCPU stack.
- There were no `429`, `401`, `409`, or `503` responses in these runs.
- Request logging was still active; see the caveats below before comparing with
  another build.

## What the seeder does

`tools/CsRaiders.Api.BenchSeeder` is a one-shot container that runs before the
API:

1. applies EF migrations;
2. seeds the system roles (`IdentitySeeder`);
3. inserts `BENCH_USER_COUNT` users (default 20,000) with emails
   `bench+0000000@bench.local` ... and the `student` role.

The password hash is computed a few times (`Bench__HashVariants`, default 64)
and reused round-robin. Hashing 20k passwords one-by-one would take minutes and
is not what we are measuring. The seeder is idempotent: it skips when enough
benchmark users exist, and replaces a partial set otherwise.

Every process must use the same `Auth__PasswordPepper__1` and Argon2 parameters,
otherwise seeded passwords will not verify.

## Scenarios

### `login`

Every request logs in as a distinct seeded user. Login is **CPU-bound by design**:
`Argon2IdPasswordHasher` serializes hashing through a semaphore sized to
`Environment.ProcessorCount`, which is 2 inside the API container. Expect a
plateau around a hundred-ish requests per second with the default Argon2 cost
(`m=8192, t=2, p=1`). That is the application's password-hashing capacity, not a
network or database limit.

### `refresh`

`setup()` logs in once per session slot (`REFRESH_SESSIONS`, default
`max(VUS, 100)`), captures the `__Host-csraiders_rt` cookie plus a device
fingerprint, and every VU then rotates its own session's token. This exercises
the real rotation path: row update, replay detection, cache invalidation, and
permission resolution. Refresh has no Argon2 work, so it is the best measure of
DB + API throughput.

Requirements:

- `VUS` must be `<= REFRESH_SESSIONS` so no two VUs refresh the same session.
- `REFRESH_SESSIONS` must be `<= BENCH_USER_COUNT`.
- The endpoint requires `Origin`, `X-Requested-With: csraiders`, and
  `Content-Type: application/json`; the scripts send all three.
- A refresh token is single-use. If a request is lost after the server rotated
  the token, the next request may hit reuse detection and fail with 401. Check
  `http_req_failed`; with a local Docker network this should be zero.

### `register`

Each iteration uses a unique email. Reusing an email returns 409 and skips
password hashing. Register is also Argon2-bound and additionally enqueues an
email; `Email__DeliveryMode=Console` keeps it off the network. Under overload the
API may return `503 capacity_exhausted` when the hashing semaphore times out.
That is a real shedding behavior, so read the status breakdown rather than
treating every non-202 as a bug.

## Knobs

Compose interpolation (set in your shell or a `.env` file):

- `BENCH_USER_COUNT` — seeded user count (default 20000). Keep it exported for
  the k6/autocannon runs so the scripts index the same users.
- `BENCH_PASSWORD`, `BENCH_EMAIL_PREFIX`, `BENCH_EMAIL_DOMAIN`
- `CRYPTO_ARGON2_MEMORY_KB`, `CRYPTO_ARGON2_ITERATIONS`,
  `CRYPTO_ARGON2_PARALLELISM` — Argon2 cost. Report these with your results.
- `RUN_ID` — set automatically by `run.sh`; unique registration emails.

Per-scenario (pass as environment variables to `run.sh`):

```bash
VUS=100 DURATION=3m tests/bench/scripts/run.sh k6 login
REFRESH_SESSIONS=250 VUS=200 DURATION=2m tests/bench/scripts/run.sh k6 refresh
VUS=50 DURATION=2m tests/bench/scripts/run.sh k6 register

CONNECTIONS=50 DURATION=10 WALLET=5000 tests/bench/scripts/run.sh autocannon login
WALLET=2000 tests/bench/scripts/run.sh autocannon refresh
```

Useful k6 knobs: `VUS`, `DURATION`, `THINK_TIME`, `USER_BLOCK` (login),
`REFRESH_SESSIONS` (refresh).

## Resetting between runs

`run.sh reset` truncates sessions, login attempts, pending registrations, reset
tokens, security events, and dead-letter emails, then restarts the API to clear
its in-memory distributed cache. Users, roles, and `app_settings` are kept.

Use `run.sh down` (deletes the volume) for a fully cold run.

## What to watch

- API CPU near 200% of one core means the 2-vCPU ceiling is the bottleneck.
- Login/register at ~100% CPU with low DB CPU confirms Argon2 is the ceiling.
- Refresh: watch Postgres CPU, connection count (pool is 50) and p95 latency.
- `429` should be zero. If present, `RateLimit__PerMinutePerIp` was not applied.
- `503` during register/login is the hashing semaphore shedding load.
- `401` during refresh means token reuse (lost rotation) or a session issue.
- `409` during register means a duplicate email (usually a missing `RUN_ID`).
- Confirm `OOMKilled=false` and that the API sees only 2 CPUs:

```bash
docker inspect --format '{{.State.OOMKilled}} {{.HostConfig.NanoCpus}} {{.HostConfig.Memory}}' \
  "$(docker compose -f tests/docker-compose-benchmark.yml ps -q api)"
```

## Autocannon refresh limitation

Autocannon has no cookie jar and cannot chain the rotated token. `smoke.mjs`
mints `WALLET` single-use refresh tokens and runs in `amount` mode, so the test
stops after exactly one pass over the wallet. It is a short smoke, not a
sustained refresh benchmark: minting the tokens is itself Argon2-bound, and the
measured window is only `WALLET` requests. Use k6 for sustained refresh load.
The scripts send cookies manually, so the `Secure` attribute never matters.

## Known measurement caveats

- **Request logging is hardcoded** (`UseSerilogRequestLogging`) and cannot be
  disabled by configuration. It adds overhead per request. EF SQL and
  application Information logs are suppressed via `Logging__LogLevel__*`, but the
  per-request Serilog line remains. Treat measured RPS as "this build as
  configured", or make logging configurable in a follow-up.
- PostgreSQL runs with `synchronous_commit=off` for benchmark throughput. Run one
  pass with it on if durability cost is part of the question.
- The distributed cache is in-memory, so the API is a single instance by design.
  Do not scale the API horizontally for these tests.
- The first requests after startup are cold (JIT, EF query compilation, buffer
  pool). k6 ramps over 20s; discard the ramp or run a warm-up first.
- Other host workloads compete for CPU. Run one scenario at a time and repeat
  measurements; report median and range, not a single run.
