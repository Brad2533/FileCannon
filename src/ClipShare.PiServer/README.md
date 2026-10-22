# ClipShare.PiServer

`ClipShare.Overlay` (the Windows hotkey/popup app) exists and works — see `src/ClipShare.Overlay`. This is the other half: the file host intended to run on a Raspberry Pi. The server is implemented and working, but so far it has only been run on a laptop, not deployed to a real Pi.

## What this service is for

The overlay app uploads an oversized Discord clip here. This service stores it on the Pi's attached HDD, hands back a public download link, and deletes the file automatically after 48 hours. A Discord bot (living in the overlay app, not here) posts that link into a channel.

## The constraint that shapes everything below

**Cloudflare Tunnel — the only way this Pi is reachable from the internet, per the "no port forwarding" decision — caps inbound POST body size at 100MB (Free/Pro) or 200MB (Business).** No workaround exists while staying on a tunnel; it's an edge-proxy limit, not something Kestrel config can override, and it only applies to inbound uploads (downloads are unaffected). Since the whole point of this tool is clips well over 100MB, a plain single-POST `/upload` endpoint would fail on exactly the files it needs to handle.

**So: uploads are chunked.** The overlay splits a file into ~80MB pieces (safely under the ceiling) and sends them one at a time to three endpoints instead of one. Everything below assumes this.

## Endpoints

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/upload/init` | `X-Api-Key` | Body `{ fileName, totalSizeBytes, chunkSizeBytes }`. Creates a GUID `uploadId`, opens `{DataRoot}/tmp/{uploadId}.part`, tracks progress in an in-memory `ConcurrentDictionary<Guid, PendingUpload>`. Returns `{ uploadId }`. |
| `POST /api/upload/{uploadId}/chunk` | `X-Api-Key` | Raw `application/octet-stream` body, one ~80MB piece, appended in order to the temp file. |
| `POST /api/upload/{uploadId}/complete` | `X-Api-Key` | Verifies received size matches expected, moves temp file to `{DataRoot}/clips/{guid}{ext}`, inserts a SQLite row, returns `{ downloadUrl, expiresAtUtc }`. |
| `GET /d/{id}` | none (public) | Serves the file via `Results.File(..., enableRangeProcessing: true)` for video-scrubbing support. 404 if missing/expired. |
| `GET /health` | none | Liveness check — used by deployment verification and the overlay's "Test Connection" button. |
| `GET /api/account` | `X-Api-Key` | "Who am I": returns the caller's `{ accountId, tier, isAdmin, label, createdAtUtc }`. |
| `POST /api/admin/accounts` | `X-Api-Key` (admin) | Body `{ tier, label? }`. Provisions an account, returns its freshly generated `fc_...` key **once**. |
| `GET /api/admin/accounts` | `X-Api-Key` (admin) | Lists accounts (no key material). |
| `DELETE /api/admin/accounts/{id}` | `X-Api-Key` (admin) | Revokes an account. Refuses to delete the caller's own. |

Pending mid-upload sessions are **not** persisted to SQLite — if the service restarts mid-transfer, the client just retries from scratch. Acceptable tradeoff for a personal-scale tool; not worth the complexity of resumable sessions.

API key check: `ApiKeyAuthFilter` (an `IEndpointFilter`) is applied to `/api/upload/...`, `/api/account`, and `/api/admin/...` (not `/d/{id}` or `/health`). It SHA-256-hashes the presented `X-Api-Key` and looks up an `Accounts` row by that digest, stashing the resolved account on the request (`HttpContext.SetAccount()` / `GetAccount()`). No constant-time compare needed — the lookup key is the digest, not the raw secret. `/api/admin/...` chains a second `AdminOnlyFilter` that requires `account.IsAdmin`.

**Accounts / tiers.** Every install authenticates as an `Accounts` row (`Id`, `ApiKeyHash`, `Tier`, `IsAdmin`, `StripeCustomerId`, `Label`, `CreatedAtUtc`) rather than sharing one secret — the prerequisite for per-account size/retention tiers and billing. On startup the configured `PiServer:ApiKey` is seeded as a Pro **admin** account if no row matches its hash, so the existing single-key overlay setup keeps working unchanged; that key is also what calls `/api/admin/*` to provision every other key. Tiers are stored as plain strings (`Free`/`Pro`); **nothing enforces them yet** — that's Phase 1/2.

## Storage layout

```
{DataRoot}/
  clips/{guid}{ext}     - final stored files
  tmp/{guid}.part        - in-progress chunked uploads
  metadata.db             - SQLite
```

```sql
CREATE TABLE IF NOT EXISTS Clips (
  Id TEXT PRIMARY KEY, OriginalFileName TEXT NOT NULL, StoredFileName TEXT NOT NULL,
  SizeBytes INTEGER NOT NULL, UploadedAtUtc TEXT NOT NULL, ExpiresAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Accounts (
  Id TEXT PRIMARY KEY, ApiKeyHash TEXT NOT NULL UNIQUE, Tier TEXT NOT NULL,
  IsAdmin INTEGER NOT NULL DEFAULT 0, StripeCustomerId TEXT NULL, Label TEXT NULL,
  CreatedAtUtc TEXT NOT NULL
);
```

SQLite via `Microsoft.Data.Sqlite`, hand-written SQL — no EF Core. Chosen over a JSON sidecar because the cleanup job needs an indexed query and SQLite gives crash-safe atomic writes without hand-rolling file locking between the upload endpoints and the cleanup timer touching the same file concurrently.

## Cleanup (48h retention)

`ClipCleanupService : BackgroundService`, `PeriodicTimer` tick every 15 minutes (plus one immediate pass at startup so a long-downtime restart doesn't leave stale files sitting around). Each tick: delete rows/files where `ExpiresAtUtc <= now`, and sweep orphaned `tmp/*.part` files whose last-activity is >2h old with no matching completed clip (abandoned/failed chunked uploads).

Expiry is read from SQLite, not tracked in an in-memory timer — a `systemctl restart` or Pi reboot resumes correctly with zero lost state.

## Things to configure explicitly (easy to forget, will fail silently/confusingly otherwise)

- **Kestrel's default max request body size (~28.6MB) must be raised** — it's below even a single 80MB chunk:
  ```csharp
  builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 100L * 1024 * 1024);
  ```
  Chunks are sent as raw octet-stream (not multipart), which also sidesteps the separate `MultipartBodyLengthLimit` default.
- **Bind Kestrel to loopback only** (`ASPNETCORE_URLS=http://127.0.0.1:5080`) — cloudflared is the only intended caller; nothing needs to reach this service directly over the LAN.

## Target framework note

The overlay app ended up on **`net10.0-windows`** rather than the `net8.0` the original plan assumed, because that's what's actually installed on the dev machine (.NET 10 SDK). Build this project on the same TFM (`net10.0`) so local end-to-end testing (`dotnet run` on Windows, per the build order below) uses one consistent runtime — then publish `linux-arm64` for the Pi using whatever .NET 10 ARM64 runtime is current at build time.

## Deployment on the Pi (once code exists and passes local testing)

1. **Raspberry Pi OS Bookworm, 64-bit (arm64)** — required for the ARM64 .NET runtime; 32-bit OS can't run it.
2. Mount the external HDD at a stable path (e.g. `/mnt/clipshare-hdd`) via `/etc/fstab` using the drive's **UUID**, not `/dev/sdaX`, so it remounts identically after reboot.
3. Install the matching **ASP.NET Core Runtime for linux-arm64** on the Pi; publish **framework-dependent** from Windows (`dotnet publish -r linux-arm64 --self-contained false`) for smaller, faster redeploys.
4. `scp` the publish output to `/opt/clipshare/piserver` on the Pi.
5. systemd unit `clipshare-piserver.service` (template to live in `deploy/`): `WorkingDirectory=/opt/clipshare/piserver`, `ExecStart=dotnet .../ClipShare.PiServer.dll`, `Restart=always`, secrets via `EnvironmentFile=/etc/clipshare/piserver.env` (chmod 600, untracked).
6. cloudflared: install arm64 package → `cloudflared tunnel login` → `cloudflared tunnel create clipshare` → `cloudflared tunnel route dns clipshare clips.<yourdomain>` → `config.yml` ingress mapping the hostname to `http://127.0.0.1:5080` → install as a systemd service.
7. Verify externally (off-LAN, e.g. phone on cellular) with `curl https://clips.<yourdomain>/health`, then confirm an actual ~80MB chunk clears the tunnel — this is what validates the chunking fix works in practice, not just in theory.

## Build order for this component

1. Shared DTOs in `ClipShare.Shared` (the three request/response shapes for init/chunk/complete — kept identical between this project and the overlay's `PiUploadClient`).
2. `Program.cs` with raised body-size limits.
3. `ClipRepository` (SQLite schema + queries).
4. `init`/`chunk`/`complete` endpoints + the API key `IEndpointFilter`.
5. `GET /d/{id}` with Range support, then `GET /health`.
6. `ClipCleanupService`.
7. Test locally end-to-end with `curl`: init → several chunk POSTs → complete → download. Verify a shortened retention window (e.g. 1 minute) survives a service restart before trusting the real 48h window.
8. Only then move to Pi deployment (§ above).

## Config/secrets

API key and any other secrets go through `EnvironmentFile=/etc/clipshare/piserver.env` on the Pi (chmod 600, untracked) — never in `appsettings.json`. For local dev before touching the real Pi, use `dotnet user-secrets` for the same key.
