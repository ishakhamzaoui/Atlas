# Atlas

ASP.NET Core (.NET 10) Web API on Clean Architecture with PostgreSQL, EF Core, MediatR, FluentValidation,
JWT bearer authentication with rotating refresh tokens, BCrypt password hashing and Serilog logging.
Login uses **username + password only** (no email). The API is served **over HTTPS only**.

```
Atlas.Domain         -> (nothing)            User, RefreshToken, roles, username/password policies, invariants
Atlas.Application    -> Domain               Commands/queries/handlers, validators, pipeline behaviors, abstractions, bootstrap
Atlas.Infrastructure -> Application, Domain  EF Core/Npgsql, BCrypt, JWT + refresh-token services, migrations
Atlas.API            -> Application, Infrastructure   Controllers, JWT bearer, policies, Swagger, Serilog, Program
```

## Configuration (nothing secret is committed)

| Setting | Environment variable | Notes |
|---|---|---|
| `ConnectionStrings:Atlas` | `ConnectionStrings__Atlas` | e.g. `Host=localhost;Port=5432;Database=atlas;Username=atlas;Password=<db-password>` |
| `Jwt:SigningKey` | `Jwt__SigningKey` | **Required**, >= 32 bytes. Generate: `openssl rand -base64 48`. App refuses to start without it. |
| `Jwt:Issuer` / `Jwt:Audience` | `Jwt__Issuer` / `Jwt__Audience` | Defaults in `appsettings.json` |
| `Jwt:AccessTokenMinutes` | `Jwt__AccessTokenMinutes` | Default 15 (1-60) |
| `Jwt:RefreshTokenDays` | `Jwt__RefreshTokenDays` | Sliding lifetime of one refresh token, default 7 (1-90) |
| `Jwt:RefreshTokenAbsoluteDays` | `Jwt__RefreshTokenAbsoluteDays` | Hard cap for a whole login session, default 30 (>= sliding, <= 365) |
| `Urls` | `ASPNETCORE_URLS` | Default `https://0.0.0.0:7000`. Any `http://` URL aborts startup. |
| `Kestrel:Certificates:Default:Path` / `KeyPath` / `Password` | `Kestrel__Certificates__Default__Path` ... | TLS certificate (PEM or PFX), see below |
| `Server:RequireHttps` | `Server__RequireHttps` | Default `true`: requests not received over TLS get 400 `https_required` |
| `Bootstrap:AdminPassword` | `Bootstrap__AdminPassword` | Only read by `--bootstrap-admin` |
| `RateLimiting:AuthPermitLimit` / `AuthWindowSeconds` | `RateLimiting__...` | Per-IP limit on login/refresh/logout/change-password (10 per 60 s) |
| `Serilog:MinimumLevel:Default` | `Serilog__MinimumLevel__Default` | See Logging |
| `MediatR:LicenseKey` | `MediatR__LicenseKey` | Optional; MediatR 13+ licensing |

Development (user-secrets, stored outside the repo):

```bash
dotnet user-secrets set "ConnectionStrings:Atlas" "Host=localhost;Port=5432;Database=atlas;Username=atlas;Password=<db-password>" --project src/Atlas.API
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/Atlas.API
```

## HTTPS only, reachable from the LAN

The server binds `https://0.0.0.0:7000` (all interfaces => reachable from the LAN) and has **no HTTP listener**:
there is no port 80/5000 and no redirect. Safeguards: startup fails if an `http://` URL is configured
(`ASPNETCORE_URLS`, launch profile, `Urls`), a middleware answers 400 `https_required` for any non-TLS request, and HSTS is sent outside Development.

**Certificate.** Kestrel needs one. For development `dotnet dev-certs https` works on the machine itself, but LAN clients will not trust it.
For the LAN create a certificate whose names match how clients address the server (replace host/IP), or use your own CA:

```bash
sudo install -d -m 750 /etc/atlas/tls
sudo openssl req -x509 -newkey rsa:4096 -sha256 -days 365 -nodes \
  -keyout /etc/atlas/tls/atlas.key -out /etc/atlas/tls/atlas.crt \
  -subj "/CN=atlas.lan" -addext "subjectAltName=DNS:atlas.lan,DNS:$(hostname),IP:192.168.1.50"
sudo chmod 640 /etc/atlas/tls/atlas.key          # readable only by the service account/group
export Kestrel__Certificates__Default__Path=/etc/atlas/tls/atlas.crt
export Kestrel__Certificates__Default__KeyPath=/etc/atlas/tls/atlas.key
```

Import `atlas.crt` into each client's trust store (or use `curl --cacert atlas.crt`). Open the port only to your LAN, e.g.
`sudo ufw allow from 192.168.1.0/24 to any port 7000 proto tcp`. If you later put a TLS-terminating reverse proxy in front,
configure forwarded headers and set `Server:RequireHttps=false` deliberately. `AllowedHosts` is `*` so any LAN name/IP works; restrict it if you prefer.

Run: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Atlas.API` (the `https` launch profile is HTTPS-only too).

## Logging

Serilog, configured entirely from configuration. Levels: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`.

| Environment | Default | Notes |
|---|---|---|
| Development (`appsettings.Development.json`) | **Debug** | also shows SQL commands (no parameter values) and authentication/authorization decisions |
| Everything else (`appsettings.json`) | Information | `Microsoft.*`/`System.*` at Warning |

What is logged: every request (Information; 4xx Warning; 5xx Error) with elapsed time, remote IP and user id; every MediatR use case
(Debug start/finish, Warning for expected rejections such as validation/conflict/unauthorized, Error for unexpected failures with the exception);
security events (logins, failed logins with a reason, password changes/resets, refresh-token reuse, admin actions). **Never logged:** passwords, tokens,
hashes, request bodies, or the attempted username above Debug level.

Change the level without code changes: `export Serilog__MinimumLevel__Default=Warning` (or edit `appsettings*.json`; level changes reload live).
Enable a rolling log file, e.g. on the server:

```bash
export Serilog__WriteTo__1__Name=File
export Serilog__WriteTo__1__Args__path=/var/log/atlas/atlas-.log
export Serilog__WriteTo__1__Args__rollingInterval=Day
export Serilog__WriteTo__1__Args__retainedFileCountLimit=14
```

## Database and migrations

Tooling: `dotnet tool install --global dotnet-ef`. Run from the repository root with the connection string exported.

```bash
# the refresh-token table (this change set) - generate once and commit the output
dotnet ef migrations add AddRefreshTokens \
  --project src/Atlas.Infrastructure --startup-project src/Atlas.API --output-dir Persistence/Migrations

dotnet ef database update --project src/Atlas.Infrastructure --startup-project src/Atlas.API

# production: review a script or ship a bundle (nothing migrates automatically at startup)
dotnet ef migrations script --idempotent -o atlas-migrate.sql --project src/Atlas.Infrastructure --startup-project src/Atlas.API
dotnet ef migrations bundle --project src/Atlas.Infrastructure --startup-project src/Atlas.API -o atlas-migrate
./atlas-migrate --connection "$ConnectionStrings__Atlas"
```

Schema: `users` (unique normalized username, check constraints, `xmin` concurrency) and `refresh_tokens`
(SHA-256 token hash unique, family id, security-stamp snapshot, expiry/absolute expiry, revoked/replaced-by, FK to users with cascade delete).

## First-run administrator bootstrap

```bash
export Bootstrap__AdminPassword='<strong temporary password, 12+ chars, upper/lower/digit/symbol>'
dotnet run --project src/Atlas.API -- --bootstrap-admin
unset Bootstrap__AdminPassword
```

Creates `admin` (role `Admin`, must change password at first login) once; does nothing if any administrator exists; never modifies existing accounts;
missing/weak secret creates nothing. Exit codes: 0 created/already initialised, 2 missing, 3 weak, 4 `admin` taken by a non-admin.

## Authentication flow and refresh tokens

1. `POST /api/v1/auth/login` returns `accessToken` (JWT, 15 min) and `refreshToken` (opaque, 7 days sliding, 30 days absolute).
   If `mustChangePassword` is `true` the access token is restricted to `change-password`/`me` and **no refresh token is issued**.
2. `POST /api/v1/auth/change-password` (temporary password as `currentPassword`) returns a fresh unrestricted pair.
3. When the access token expires: `POST /api/v1/auth/refresh` with `{ "refreshToken": "..." }` returns a **new access token and a new refresh token**.
4. `POST /api/v1/auth/logout` with the refresh token ends that session (204 always).

Refresh-token lifecycle and storage:

* 256-bit random, base64url. Only the **SHA-256 hash** is stored; the plaintext exists only in the response that issues it, so keep it in secure storage on the client and never log it.
* **Single use (rotation):** each refresh retires the presented token and issues a successor in the same *family* (one family = one login).
  Presenting an already-used token is treated as theft: the whole family is revoked (both the thief and the victim must log in again). Simultaneous use of one token is arbitrated by optimistic concurrency, so at most one request wins.
* A session can never outlive `RefreshTokenAbsoluteDays`, no matter how often it is refreshed.
* All of a user's refresh tokens are revoked on password change, admin password reset, deactivation, role change and rename (security-stamp rotation also invalidates them if revoking were ever missed).
* Logout revokes only that device's session. Already-issued access tokens remain valid until they expire (<= 15 min) unless the stamp rotates.
* An hourly background job deletes tokens that expired more than 7 days ago.
* Sent in the JSON body (no cookies), so there is no CSRF surface; use HTTPS (enforced) and keep tokens out of URLs.

## Endpoints

| Method | Route | Access |
|---|---|---|
| POST | `/api/v1/auth/login` | anonymous (rate limited) |
| POST | `/api/v1/auth/refresh` | anonymous, needs a valid refresh token (rate limited) |
| POST | `/api/v1/auth/logout` | anonymous, needs the refresh token (rate limited) |
| POST | `/api/v1/auth/change-password` | any valid access token (rate limited) |
| GET | `/api/v1/auth/me` | any valid access token |
| POST/GET | `/api/v1/admin/users` | Admin |
| GET/PUT | `/api/v1/admin/users/{id}` | Admin |
| PATCH | `/api/v1/admin/users/{id}/status` | Admin |
| POST | `/api/v1/admin/users/{id}/reset-password` | Admin (one-time temporary password) |

```json
POST /api/v1/auth/login     { "username": "admin", "password": "<password>" }
POST /api/v1/auth/refresh   { "refreshToken": "<refresh-token>" }
POST /api/v1/auth/logout    { "refreshToken": "<refresh-token>" }
POST /api/v1/auth/change-password { "currentPassword": "<current>", "newPassword": "<new>", "confirmNewPassword": "<new>" }
```

Errors are RFC 7807 `application/problem+json` with a stable `code` (`validation_failed`, `invalid_credentials`, `invalid_refresh_token`, `username_taken`, `last_active_admin`, `https_required`, ...).
Status codes: 400, 401, 403, 404, 409 conflict, 422 business rule, 429 rate limited.

## Swagger

In `Development`: `https://<host>:7000/swagger`. Call `login`, click **Authorize**, paste the `accessToken`. Not mapped in other environments.

## Build and test

```bash
dotnet build Atlas.slnx
dotnet test Atlas.slnx
```

### Running the integration tests natively on the VM (no Docker)

Create a **dedicated** test database once (script needs `sudo -u postgres`):

```bash
./scripts/create-test-db.sh            # prints the export line below with a generated password
export ATLAS_TEST_DB='Host=localhost;Port=5432;Database=atlas_test;Username=atlas_test;Password=<test-db-password>'
dotnet test Atlas.slnx
```

Equivalent manual SQL: `CREATE ROLE atlas_test LOGIN PASSWORD '<pw>'; CREATE DATABASE atlas_test OWNER atlas_test;`

How the fixtures use it: when `ATLAS_TEST_DB` is set, every fixture (each Infrastructure test class, the API factory) creates its **own throw-away schema**
inside that database, builds it from the real EF migrations (or from the model if no migration exists yet), points the app under test at it, and drops the schema afterwards.
Fixtures and test projects can therefore run in parallel without sharing data, and nothing persists between runs. The database name **must contain "test"**, otherwise the tests refuse to run.
If `ATLAS_TEST_DB` is not set, the fixtures fall back to a disposable PostgreSQL container (requires Docker). The test database is separate from the development database, and the connection string is never committed.
