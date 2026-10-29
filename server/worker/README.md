# Mica API on Cloudflare Workers + D1

Deployable alternative to the Node/SQLite server in the parent directory. Same /v1 API and encrypted vault format; the existing Mica desktop client already points at https://the-api.lessnoise.online. No VPS, Docker or filesystem database is needed.

## Deploy

Prerequisites: Node.js 24 for tooling, a Cloudflare account with lessnoise.online as an active zone, and a Discord OAuth application.

Run from this directory:

```sh
npm ci
npx wrangler login
npx wrangler d1 create mica-sync
```

Copy the returned database_id into wrangler.jsonc, replacing REPLACE_WITH_D1_DATABASE_ID. The database binding must stay named DB. If necessary, set account_id in the configuration to select the intended Cloudflare account.

In the Discord Developer Portal, register exactly:

```text
https://the-api.lessnoise.online/v1/auth/discord/callback
```

Then:

```sh
npm run db:remote
npm run deploy
npx wrangler secret put DISCORD_CLIENT_ID
npx wrangler secret put DISCORD_CLIENT_SECRET
```

Enter values at the prompts, not in source or shell arguments. The Worker returns 503 configuration_required until both values exist. The configuration attaches the-api.lessnoise.online as a Workers Custom Domain and disables workers.dev/preview endpoints. Cloudflare manages HTTPS for the custom domain. Resolve an existing DNS record or Worker association for that hostname before attaching the new Worker; do not create a VPS proxy record.

Verify:

```sh
curl https://the-api.lessnoise.online/healthz
```

Expected: {"status":"ok"}. Then complete Discord sign-in from Mica and test Pull/Push with a vault. Real Discord credentials, DNS ownership and production deployment must be supplied in your own account; they were not used in local verification.

A Custom Domain routes the whole hostname to this Worker. If an existing Cloudflare setup specifically needs a route instead, replace routes with [{"pattern":"the-api.lessnoise.online/*","zone_name":"harumi.io.vn"}] and configure the required proxied DNS record separately. Use one model, not both.

## Local development

```sh
npm ci
npm run db:local
```

Copy .dev.vars.example to .dev.vars, supply development Discord credentials, then npm run dev. The public callback remains the configured HTTPS URL: a local emulator alone cannot receive the production Discord redirect. Use the mocked automated tests for local OAuth, or an explicitly configured development HTTPS callback/domain for manual OAuth.

```sh
npm test
npm run check
```

Six tests cover OAuth state/proof/replay/denial/expiry, one-use concurrent polling, account isolation, initial and update write races, logout, request limits, D1 state across fresh handlers and scheduled cleanup. Tests use actual local D1 bindings from Miniflare; the last test executes the complete module inside workerd and stores/reads a roughly 1.87 MB encrypted envelope. Discord is mocked for integration tests. The dry run bundles only, without publishing.

Tool versions are pinned by package-lock.json. The compatibility date is 2026-10-19, supported by the tested runtime.

## Storage and behavior

- D1 holds users, OAuth requests, hashed bearer tokens and encrypted vault envelopes. It never receives the master password or decrypted vault.
- OAuth starts expire after five minutes; sessions expire after 30 days.
- Polling uses an atomic two-second gate and a transactional conditional insert/delete to deliver a token once.
- Vault writes use a single conditional SQL statement per update. A stale version returns 409, including concurrent first writes.
- Shared D1 rate counters replace process-local maps: 20 starts/IP/minute, 1,000 requests/IP/minute, 120 authenticated requests/user/minute. IP keys hash Cloudflare's CF-Connecting-IP, not arbitrary forwarded headers. This uses D1 writes and should be included in your quota/cost planning.
- Cron runs every 15 minutes to prune expired requests, sessions and rate counters. Expiry checks apply immediately, independently of cleanup.
- 2 MiB JSON limit for vault PUT, 8 KiB for other JSON bodies. The desktop protocol and envelope validation are unchanged; see ../README.md.
- Cross-origin browser requests are rejected. Responses are no-store; callback HTML contains no token.
- Worker observability is disabled to avoid persisting callback URLs containing OAuth codes. Do not add token/body/query logging. Platform/account logging is managed separately.

## Account deletion

Google Play links https://lessnoise.online/delete-account, which points here.

- `GET /v1/account/delete` starts a Discord sign-in in any browser (no app needed). After Discord, the page shows the account name and a Delete account button; `POST /v1/account/delete/confirm` then removes the user's sent tabs, devices, settings, vault, sessions, requests and user row in one transaction. A Discord user without a Mica account gets "No Mica account" and nothing is created. The page token is single-use and expires with the sign-in (10 minutes).
- `DELETE /v1/account` does the same for a signed-in app (bearer token).
- Mail: in the Cloudflare dashboard, Email Routing for lessnoise.online, route `accounts@lessnoise.online` to this Worker ("Send to a Worker"). The `email` handler replies once per sender per day with the deletion link and never deletes anything from mail, because an email can't prove which Discord account is someone's. Auto-submitted, bulk and mailer-daemon mail gets no reply. To also get a copy, verify an address under Email Routing → Destination addresses and set it as the `DELETE_FORWARD_TO` variable.

Apply `migrations/0007_account_delete.sql` (`npm run db:remote`) before deploying this version.

## Existing Node deployment and backups

This creates a new database; it does not automatically copy the parent server's SQLite file. Keep the old deployment and backups until you intentionally migrate. For a fresh installation users sign in again, then explicitly upload their local encrypted vault after Pull. Do not replace an existing populated database with an empty migration.

For an existing populated service, stop writes and back up first. Export/import users and vaults with a reviewed SQLite-to-D1 data migration, preserving versions and ciphertext exactly; expire old sessions and have users sign in again. No production data migration was performed here.

Use D1 export/Time Travel for backup and recovery. An export can be made with:
```sh
npx wrangler d1 export DB --remote --output mica-backup.sql
```
Treat backups as private account data even though vault payloads are encrypted.

References:
- https://developers.cloudflare.com/d1/worker-api/d1-database/
- https://developers.cloudflare.com/d1/get-started/
- https://developers.cloudflare.com/workers/configuration/routing/custom-domains/
- https://developers.cloudflare.com/workers/configuration/secrets/
