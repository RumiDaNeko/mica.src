# Mica sync API

**Cloudflare Workers deployment:** use [worker/README.md](worker/README.md) for the Workers + D1 implementation, migrations and deployment commands. The Node/Docker option below remains available.

Standalone Node.js 24 backend for `https://the-api.lessnoise.online`. No third-party runtime packages. It stores Discord identities, hashed session tokens and encrypted vault envelopes in SQLite. It never receives the vault passphrase or decrypted passwords. Deployment and real Discord authorization have not been performed.

## Configure and run

1. Create a Discord application in the [Discord Developer Portal](https://discord.com/developers/applications). Under OAuth2, register this **exact redirect URL**: `https://the-api.lessnoise.online/v1/auth/discord/callback`. Only the `identify` scope is requested; no bot is needed.
2. Copy `.env.example` to `.env` and replace both Discord placeholders. The client secret belongs only on this server. Protect `.env` and never bundle it with the desktop app.
3. Install Node.js 24. From this directory, run `npm start` (or `node --env-file=.env index.mjs`). No `npm install` is needed. Missing credentials fail startup. For Docker, run `docker compose up -d --build`; its named volume retains SQLite across restarts.
4. Point the domain's DNS at your server. Obtain a valid TLS certificate and adapt `nginx.conf.example` to your nginx installation. The example binds the upstream to loopback and terminates HTTPS on nginx. Test nginx configuration before reloading. Keep port 3000 private; only 80/443 should be public.
5. Check `/healthz` through HTTPS and complete a real desktop sign-in to verify the configured client ID, secret and redirect together.

The example nginx config suppresses access logs because callback query strings contain short-lived OAuth codes. Keep those URLs out of any upstream CDN/proxy logging, too. The application does not log request headers, codes or tokens. It ignores forwarded IP headers. With this proxy configuration the application's IP limits apply to the proxy as a whole (20 starts/minute and 1,000 requests/minute), while nginx separately limits each network client. Adjust limits deliberately for larger installations; do not trust arbitrary `X-Forwarded-For` values.

Run a single application instance per SQLite database. Back up the database while stopped, or use SQLite's online backup facility; copying only the main database during live WAL activity is insufficient. Preserve the encrypted vault database and secure backup access. Updating the Node 24 image regularly applies security updates. Docker uses an unprivileged user, a read-only root filesystem and a writable data volume. Native execution should also use a dedicated unprivileged OS account.

## Desktop API contract

JSON responses have `Cache-Control: no-store`. Errors use `{ "error": "code" }`; HTTP 429 includes `Retry-After: 2`. No wildcard CORS, cookies or browser-stored login tokens are required. Cross-origin browser requests are denied. Send `Content-Type: application/json` for JSON request bodies.

1. Generate a cryptographically random 32-byte verifier and encode as unpadded base64url (43 characters). Compute SHA-256 of that **encoded verifier string**, then encode the hash as unpadded base64url to obtain `challenge`.
2. `POST /v1/auth/discord/start` with `{challenge}` returns `{requestId, authorizationUrl, expiresAt}`; `expiresAt` is ISO 8601 UTC, five minutes from creation. Open `authorizationUrl` in the system browser. Retain the verifier only in the requesting desktop app.
3. `POST /v1/auth/discord/poll` with `{requestId,verifier}` at most once every two seconds. Response is `{status:"pending"}` until callback completion, then `{status:"ready",token,user:{id,username}}`. Delivery is one-use. If that successful response is lost, start a fresh login. Failed/expired requests cannot yield a token; start again. The browser callback only displays completion text, with no application token in HTML or a URL.
4. Use `Authorization: Bearer <token>` on every account/vault/logout request. Tokens expire after 30 days; securely store them using the operating system's credential protection.

| Endpoint | Response / behavior |
| --- | --- |
| `GET /v1/account` | `{user:{id,username}}` |
| `POST /v1/auth/logout` | `{status:"ok"}`; immediately revokes the current token |
| `GET /v1/vault` | `{version:0,envelope:null}` initially, otherwise stored `{version,envelope}` |
| `PUT /v1/vault` | Body `{version,envelope}`; returns `{version:version+1}`. HTTP 409 `version_conflict` means fetch and reconcile before retrying. Never blindly overwrite. |

The vault payload has exactly these fields:

```json
{
  "format": 1,
  "kdf": "PBKDF2-SHA256",
  "iterations": 600000,
  "salt": "standard padded base64 of 16 bytes",
  "iv": "standard padded base64 of 16 bytes",
  "ciphertext": "standard padded base64 of nonempty AES-CBC ciphertext",
  "mac": "standard padded base64 of 32 bytes"
}
```

The server checks field names, types, canonical base64 and byte lengths only. Cryptographic key derivation, authentication and decryption belong to the desktop client. Ciphertext length must be a nonzero multiple of 16 bytes. The complete PUT JSON body is limited to 2 MiB; other JSON bodies to 8 KiB. A version must be a nonnegative safe integer. Vault records are keyed exclusively by the authenticated Discord user ID, never by an ID supplied in a request body.

## Verification

Run `npm test` on Node.js 24. Integration tests open temporary loopback HTTP servers and mock Discord responses; they never contact Discord or the production domain. They cover state replay, denied authorization, invalid possession proof, polling limits, expiry, logout, account isolation, conflicting writes, payload limits, cross-origin rejection and persistence across restart. The server factory accepts an injected fetch implementation and clock for tests; the deployed entry point always uses real fetch and requires credentials.

Protocol references: [Discord OAuth2](https://discord.com/developers/docs/topics/oauth2) and [Node.js 24 SQLite](https://nodejs.org/download/release/latest-v24.x/docs/api/sqlite.html).
