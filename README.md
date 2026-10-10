# Atlas

ASP.NET Core (.NET 10) Web API on Clean Architecture with PostgreSQL, EF Core, MediatR, FluentValidation,
JWT bearer authentication and BCrypt password hashing. Login uses **username + password only** (no email).

```
Atlas.Domain         -> (nothing)            User, roles, username/password policies, invariants
Atlas.Application    -> Domain               Commands/queries/handlers, validators, abstractions, bootstrap service
Atlas.Infrastructure -> Application, Domain  EF Core/Npgsql, BCrypt, JWT issuing, migrations
Atlas.API            -> Application, Infrastructure   Controllers, JWT bearer, policies, Swagger, Program
```

## Configuration (nothing secret is committed)

| Setting | Environment variable | Notes |
|---|---|---|
| `ConnectionStrings:Atlas` | `ConnectionStrings__Atlas` | e.g. `Host=localhost;Port=5432;Database=atlas;Username=atlas;Password=<db-password>` |
| `Jwt:SigningKey` | `Jwt__SigningKey` | **Required**, >= 32 bytes. Generate: `openssl rand -base64 48`. App refuses to start without it. |
| `Jwt:Issuer` / `Jwt:Audience` | `Jwt__Issuer` / `Jwt__Audience` | Defaults in `appsettings.json` (`atlas-api` / `atlas-clients`) |
| `Jwt:AccessTokenMinutes` | `Jwt__AccessTokenMinutes` | Default 15 (allowed 1-60). No refresh tokens. |
| `Bootstrap:AdminPassword` | `Bootstrap__AdminPassword` | Only read by `--bootstrap-admin`; see below |
| `RateLimiting:AuthPermitLimit` / `AuthWindowSeconds` | `RateLimiting__...` | Per-IP limit on login/change-password (default 10/60 s) |
| `MediatR:LicenseKey` | `MediatR__LicenseKey` | Optional; MediatR 13+ licensing |

Development (user-secrets, stored outside the repo):

```bash
dotnet user-secrets set "ConnectionStrings:Atlas" "Host=localhost;Port=5432;Database=atlas;Username=atlas;Password=<db-password>" --project src/Atlas.API
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/Atlas.API
```

Production: provide the same values as environment variables or from your secret store. In production
set `ASPNETCORE_ENVIRONMENT=Production` (Swagger is then not mapped).

## Database and migrations

Tooling: `dotnet tool install --global dotnet-ef`. Run from the repository root with the connection string
exported (`export ConnectionStrings__Atlas='...'`).

```bash
# create the migration (once; commit the generated files)
dotnet ef migrations add InitialUserManagement \
  --project src/Atlas.Infrastructure --startup-project src/Atlas.API \
  --output-dir Persistence/Migrations

# apply (development)
dotnet ef database update --project src/Atlas.Infrastructure --startup-project src/Atlas.API

# production: review a script, or ship a self-contained bundle (nothing migrates automatically at startup)
dotnet ef migrations script --idempotent -o atlas-migrate.sql \
  --project src/Atlas.Infrastructure --startup-project src/Atlas.API
dotnet ef migrations bundle --project src/Atlas.Infrastructure --startup-project src/Atlas.API -o atlas-migrate
./atlas-migrate --connection "$ConnectionStrings__Atlas"
```

Schema: table `users` with unique index on `normalized_username` (case-insensitive uniqueness), check constraints
for role, username format and normalization, and an `xmin` optimistic-concurrency token.

## First-run administrator bootstrap

Approach: an explicit, one-time CLI command that reads a **temporary** password from the environment.

```bash
export Bootstrap__AdminPassword='<strong temporary password, 12+ chars, upper/lower/digit/symbol>'
dotnet run --project src/Atlas.API -- --bootstrap-admin
unset Bootstrap__AdminPassword
```

* Creates user `admin` (role `Admin`, `MustChangePassword = true`). Prints a status line, never the secret.
* Idempotent and race-safe: does nothing if **any** administrator exists; concurrent runs are arbitrated by the unique index.
* Never resets or modifies an existing account (a pre-existing non-admin `admin` is left untouched and reported).
* Missing or weak secret => nothing is created (exit code 2 / 3). The normal web host never reads this setting.
* Exit codes: 0 created/already initialised, 2 missing secret, 3 weak secret, 4 `admin` name taken by a non-admin.

## Authentication flow

1. `POST /api/v1/auth/login` -> JWT. The first time, `mustChangePassword` is `true` and the token is restricted
   to `change-password` and `me` (all other endpoints return 403).
2. `POST /api/v1/auth/change-password` with the temporary password as `currentPassword`. Returns a fresh, unrestricted token;
   all earlier tokens stop working (security stamp rotation).
3. Use `Authorization: Bearer <token>`.

Every request validates signature, issuer, audience and lifetime, and then checks that the user is still active and the
token's security stamp is current (so deactivation, password changes/resets, role changes and renames take effect immediately).

## Endpoints

| Method | Route | Access |
|---|---|---|
| POST | `/api/v1/auth/login` | anonymous (rate limited) |
| POST | `/api/v1/auth/change-password` | any valid token (rate limited) |
| GET | `/api/v1/auth/me` | any valid token |
| POST | `/api/v1/admin/users` | Admin |
| GET | `/api/v1/admin/users?page=&pageSize=&search=&role=&isActive=` | Admin |
| GET | `/api/v1/admin/users/{id}` | Admin |
| PUT | `/api/v1/admin/users/{id}` | Admin (username, role) |
| PATCH | `/api/v1/admin/users/{id}/status` | Admin |
| POST | `/api/v1/admin/users/{id}/reset-password` | Admin (returns a one-time temporary password) |

Examples (placeholders only):

```json
POST /api/v1/auth/login            { "username": "admin", "password": "<password>" }
POST /api/v1/auth/change-password  { "currentPassword": "<current>", "newPassword": "<new>", "confirmNewPassword": "<new>" }
POST /api/v1/admin/users           { "username": "jdoe", "password": "<temporary-password>", "role": "User" }
PUT  /api/v1/admin/users/{id}      { "username": "jdoe", "role": "Admin" }
PATCH /api/v1/admin/users/{id}/status { "isActive": false }
```

Errors are RFC 7807 `application/problem+json` with a stable `code` (`validation_failed`, `username_taken`,
`last_active_admin`, `invalid_credentials`, ...). Status codes: 400 validation, 401 unauthenticated/invalid credentials,
403 forbidden, 404, 409 conflict, 422 business rule, 429 rate limited.

Rules: only admins manage users; no self-registration; usernames are 3-32 chars `[A-Za-z0-9._-]`, case-insensitively unique;
passwords are 12-128 chars with upper/lower/digit/symbol and must not contain the username; the last active admin cannot be
deactivated or demoted, admins cannot deactivate/demote/reset themselves.

## Swagger

In `Development` open `https://localhost:7290/swagger` (or `http://localhost:5264/swagger`), call `login`, click **Authorize**
and paste the `accessToken`. Swagger is not mapped in other environments. `Atlas.API.http` has ready-made requests.

## Build and test

```bash
dotnet build Atlas.slnx
dotnet test Atlas.slnx
```

Infrastructure and API integration tests start a throwaway PostgreSQL container with Testcontainers, so **Docker must be running**.
They use random secrets and never touch a real database. Once a migration exists the tests apply it; before that they use `EnsureCreated`.
