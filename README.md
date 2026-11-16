# TrackLab API (.NET backend)

Backend for **TrackLab**, a web app for tracking lab analysis requests. It sits between the Next.js frontend and the assessment API and adds what the brief asks of the backend:

- **Auth gateway**: login, logout, current user and automatic token refresh. Tokens are kept in encrypted HttpOnly cookies and never reach the browser.
- **CRUD gateway** for Master Trackers, Transaction Requests and Transaction Trackings, with input validation.
- **Logging** (feature 4): structured JSON logs with a correlation ID per request, an endpoint that collects frontend errors, and an endpoint to read logs back.
- **Audit trail** (bonus BE): every create, update and delete is recorded with the user, time and field-by-field changes.

Frontend repository: see `tracklab-frontend` (Next.js).

---

## Architecture

```
Browser ──► Next.js (localhost:3000) ──/api/*──► TrackLab.Api (localhost:5080) ──► Assessment API
                                                 │                                (api-assessment-biopharma.kalbe.co.id)
                                                 ├─ logs/tracklab-YYYYMMDD.jsonl   (structured logs)
                                                 └─ data/audit-trail.jsonl         (audit trail)
```

- The browser only talks to the Next.js app. Next.js forwards `/api/*` to this backend, so the session cookies are first-party and no CORS setup is needed.
- This backend adds `app_id` / `app_key` to every call to the assessment API. Those secrets never reach the browser.
- After login, the access token, the refresh token and the user's name are stored in **encrypted, signed HttpOnly cookies** (ASP.NET Core Data Protection). The browser can't read them and can't tamper with them.
- Right after login, the gateway calls `GET /auth/authorization`. The assessment API only accepts a new token on its feature endpoints once this call has been made (see [Findings](#findings-about-the-assessment-api)).
- Access tokens last one hour. The gateway refreshes a token shortly before it expires. On a `401` it first re-activates the token and retries. If that still fails, it refreshes the token once with the `RefreshToken` cookie and retries again.

## Tech stack

- .NET 8, ASP.NET Core Web API (controllers)
- No third-party runtime packages: logging, rate limiting, data protection and health checks all come from the framework
- Tests: xUnit and `Microsoft.AspNetCore.Mvc.Testing` (in-memory integration tests with a fake assessment API)

---

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`dotnet --version` should print 8.x)

### 1. Configure secrets

The app credentials from the assessment brief go in a git-ignored `.env` file:

```bash
cd src/TrackLab.Api
cp .env.example .env
# then edit .env:
#   UpstreamApi__AppId=<app_id from the brief>
#   UpstreamApi__AppKey=<app_key from the brief>
```

Real environment variables override `.env`, so in CI or production you can set `UpstreamApi__AppId` / `UpstreamApi__AppKey` directly instead. The app refuses to start, with a clear message, if either value is missing.

### 2. Run

```bash
dotnet run --project src/TrackLab.Api
```

The API listens on **http://localhost:5080**. Check it with:

```bash
curl http://localhost:5080/health   # -> Healthy
```

Then start the frontend and log in with the account from the brief (`assessment.it`).

### 3. Database migrations

**None needed.** All business data lives in the assessment API. This service keeps only two local files, which are created automatically on first run:

| File | Content |
|---|---|
| `src/TrackLab.Api/logs/tracklab-YYYYMMDD.jsonl` | Structured logs, one JSON object per line, one file per day (14 days kept) |
| `src/TrackLab.Api/data/audit-trail.jsonl` | Append-only audit trail |

The audit trail sits behind the `IAuditTrailStore` interface. Moving it to a database (for example EF Core with SQL Server) means adding one class and changing one line in `Program.cs`.

### 4. Test

```bash
dotnet test
```

- **Unit tests**: token parsing, paging header mapping, audit diffing, `.env` parsing, correlation ID validation.
- **Integration tests**: start the real app in memory with `WebApplicationFactory` and replace the assessment API with a recorded fake. They cover login and cookies, CSRF, validation, paging headers, `createdBy`/`updatedBy` from the session, soft vs hard delete, audit entries, token refresh and retry, upstream errors, logout and frontend log intake.

---

## API reference

All endpoints except login, logout, the client log and health require a session. Requests that change data (`POST`, `PUT`, `DELETE`) must send `X-Requested-With: XMLHttpRequest` (CSRF protection; see [Security](#security)).

### Auth

| Method | Path | Description |
|---|---|---|
| POST | `/api/auth/login` | `{ "username", "password" }`. Sets the session cookies and returns `{ username, displayName }` |
| POST | `/api/auth/logout` | Logs out upstream and clears the cookies |
| GET | `/api/auth/me` | Current user, plus roles and modules from `/auth/authorization` |
| POST | `/api/auth/refresh` | Refresh now (normally automatic) |

### Trackers, requests, trackings

`{resource}` is `trackers`, `requests` or `trackings`.

| Method | Path | Upstream call | Notes |
|---|---|---|---|
| GET | `/api/{resource}?offset&limit&sortBy&sortOrder&search` | `GET /{resource}/paging` | Query values are sent as `X-Paging-*` headers. `limit` is 1–100 and `sortBy` must be a property name. |
| GET | `/api/{resource}/advanced?…&include&exclude&operator&ignoreCase` | `GET /{resource}/paging/advance` | `include`/`exclude` are JSON such as `{"last_status":"A\|\|B"}`; the backend Base64-encodes them |
| GET | `/api/{resource}/all` | `GET /{resource}` | Unpaged, for dropdowns |
| GET | `/api/{resource}/{id}` | `GET /{resource}/{id}` | |
| POST | `/api/{resource}` | `POST /{resource}` | `id` and `createdBy` are set by the server |
| PUT | `/api/{resource}/{id}` | `PUT /{resource}` | `updatedBy` is set by the server |
| DELETE | `/api/{resource}/{id}` | `PUT /{resource}` with `isActive: false` | Deactivates the record (soft delete). The assessment API's DELETE endpoints don't work (see Findings). With `UpstreamApi:UseDeleteEndpoint=true` the backend calls `DELETE /{resource}` instead and supports `?hard=true`. |
| GET | `/api/trackers/code/{code}` | `GET /trackers/code/{code}` | |
| GET | `/api/requests/{id}/trackings` | `GET /trackings/request-id/{id}` | Tracking progress of one request |

Successful responses are passed through unchanged. Errors are returned as RFC 9457 **ProblemDetails** with a `traceId`:

| Situation | Status |
|---|---|
| Invalid input | `400` with field errors (the assessment API is not called) |
| No session, or the session has expired and can't be refreshed | `401` |
| Missing CSRF header | `403` |
| Assessment API error (5xx) | `502` (upstream details are logged, not returned) |
| Assessment API unreachable or too slow | `502` / `504` |
| Too many login attempts | `429` |

### Logging and audit trail

| Method | Path | Description |
|---|---|---|
| POST | `/api/logs/client` | Frontend error/event `{ level, message, url?, stack?, context? }` (anonymous, rate limited) |
| GET | `/api/logs?date&minLevel&search&correlationId&limit` | Read log entries, newest first |
| GET | `/api/audit-trails?entity&entityId&user&action&offset&limit` | Audit entries, newest first |
| GET | `/api/audit-trails/{id}` | One audit entry |
| GET | `/health` | Health check |

---

## Logging

- Every request gets a **correlation ID**: taken from the `X-Correlation-Id` request header, or generated. It is returned in the response and attached to every log line written while handling that request, including the upstream call. The frontend sends its own ID with each call, so one ID links a browser error, the backend request and the assessment API call.
- Logs go to the console and to `logs/tracklab-YYYYMMDD.jsonl`:
  ```json
  {"timestamp":"2026-11-16T13:48:42+07:00","level":"Information","category":"TrackLab.Api.Logging.RequestLoggingMiddleware","message":"HTTP GET /api/trackers responded 200 in 31 ms for assessment.it from 127.0.0.1","correlationId":"dde5178a…","properties":{"Method":"GET","StatusCode":200,"ElapsedMs":31,…}}
  ```
- What is logged: each HTTP request (method, path, status, duration, user, IP), each upstream call (status, duration), login/logout/refresh events, audit events, frontend errors and unhandled exceptions.
- What is **not** logged: request bodies, passwords, tokens and headers.
- Log writes go into a queue that a background task flushes to disk, so logging doesn't slow requests down. Files older than 14 days are deleted.

## Audit trail

For every successful create, update or delete, the backend saves:

```json
{
  "id": "…", "timestamp": "2026-11-16T06:48:10Z", "user": "assessment.it",
  "action": "Update", "entity": "tracker", "entityId": "…",
  "changes": [{ "field": "description", "oldValue": "QA Checked", "newValue": "QA Checked & Approved" }],
  "before": { … }, "after": { … },
  "correlationId": "…", "clientIp": "…", "userAgent": "…"
}
```

The "before" snapshot is read from the assessment API just before the change, so the diff reflects what was really stored. Entries are only ever appended, never edited. A failure to write the audit entry is logged, but the user's change still succeeds.

## Security

| Concern | Measure |
|---|---|
| Secrets | `app_id` / `app_key` live only on the server (`.env` or environment variables, git-ignored) |
| Token theft (XSS) | Tokens are in HttpOnly cookies, never in JavaScript or `localStorage` |
| Cookie tampering | Cookies are encrypted and signed (Data Protection); a modified cookie counts as logged out |
| CSRF | `SameSite=Strict` cookies, plus a required `X-Requested-With` header on state-changing requests |
| Brute force | Login is limited to 10 attempts per minute per IP |
| Injection into upstream headers and logs | Paging and sort values are validated, filters must be flat JSON objects, correlation IDs are restricted to `[A-Za-z0-9-]`, and line breaks are stripped from client log messages |
| Mass assignment | `createdBy` / `updatedBy` / `deletedBy` and `id` are set by the server, never taken from the client |
| Error leakage | Upstream 5xx bodies and exception details are logged, not returned |
| Default deny | Every endpoint requires a session unless marked `[AllowAnonymous]` |
| Headers | `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Cache-Control: no-store` |

## Configuration

`src/TrackLab.Api/appsettings.json` (override any value with an environment variable, using `__` for `:`):

| Key | Default | Meaning |
|---|---|---|
| `UpstreamApi:BaseUrl` | `https://api-assessment-biopharma.kalbe.co.id` | Assessment API |
| `UpstreamApi:AuthPath` / `FeaturePath` | `/api/v1/authentication` / `/api/v1/feature` | Base paths |
| `UpstreamApi:AppId` / `AppKey` | *(required, via `.env`)* | App credentials |
| `UpstreamApi:AppCode` / `ModuleCode` | `ASSESSMENT` / `ALL` | Sent at login and logout |
| `UpstreamApi:RefreshCookieName` | `RefreshToken` | Cookie the refresh endpoint reads |
| `UpstreamApi:UseDeleteEndpoint` | `false` | Use the API's DELETE endpoints instead of deactivating via PUT |
| `UpstreamApi:TimeoutSeconds` | `25` | Upstream timeout (kept below the Next.js proxy's 30 s) |
| `SessionCookies:Secure` | `true` (`false` in Development) | Send cookies over HTTPS only |
| `Cors:AllowedOrigins` | `["http://localhost:3000"]` | Only needed if the frontend calls the API directly |
| `FileLogging:Directory` / `MinimumLevel` / `RetainedDays` | `logs` / `Information` / `14` | File logs |
| `AuditTrail:FilePath` | `data/audit-trail.jsonl` | Audit trail file |

## Project structure

```
src/TrackLab.Api/
├── Program.cs              # Composition root: services and middleware order
├── Controllers/            # Auth, Trackers, Requests, Trackings, AuditTrails, Logs
├── Contracts/              # Request DTOs with validation attributes
├── Upstream/               # Typed HttpClient, credential/logging handlers, token refresh gateway, paging headers
├── Auth/                   # Encrypted cookie session + ASP.NET Core authentication handler
├── Audit/                  # Audit models, diff, recorder, JSON-lines store
├── Logging/                # Correlation ID, request logging, JSON file logger + reader
├── Security/               # CSRF header check, security headers, rate limit policies
└── Errors/                 # Global exception handler (ProblemDetails)
tests/TrackLab.Api.Tests/
├── Unit/                   # Pure logic
├── Integration/            # Whole app in memory against a fake assessment API
└── Support/                # Test factory, fake upstream, JWT helper
```

## Findings about the assessment API

The Postman collection has no example responses, so the API's behaviour was checked by hand. The backend is built around what that turned up:

| Finding | How it's handled |
|---|---|
| Login returns `{ data: { token: { access, refresh, expiresIn: 3600 } } }` and also sets a `RefreshToken` cookie | Both tokens are kept in the encrypted session. The refresh call sends the `RefreshToken` cookie (with the token from the body as a fallback). |
| **A new token gets `401` on every feature endpoint until `GET /auth/authorization` has been called with it** | Called automatically right after login and after each refresh. On a `401`, the gateway re-activates the token before trying a refresh. |
| `X-Paging-SortBy` becomes a SQL `ORDER BY` column: a missing value or an unknown column returns HTTP 500. The columns don't fully match the diagram (requests have no `created_on`; the date column is `request_date`) | The backend always sends a sort column (default `id`) and only accepts plain property names. If the API rejects a column (Postgres error 42703), the backend fetches the page again sorted by `id` and sets `X-Sort-Fallback`, so the user still gets data. |
| Paging responses are `{ data: { items, totalItems, totalPages, pageCurrent, ... } }` | Passed through; the frontend reads `items` and `totalItems`. |
| Sending `null` for some optional fields fails (e.g. `"mandays": null` on a tracker gives *"could not be converted to System.Int32"*), while leaving the field out works | Empty optional fields are omitted from the JSON sent to the API. |
| A `DELETE` whose JSON body is streamed (chunked, no `Content-Length`) hangs until the timeout | Bodies are serialised up front, so every request carries a `Content-Length`. |
| **Every `DELETE` fails with 400 "A non-empty request body is required."**: the body never reaches the API, even from Postman, so it is dropped before the API sees it. A request sent right after it on the same connection can then hang. | By default, deletes are done as a `PUT` with `isActive: false` (built from the record's current data) and recorded in the audit trail as deletes. Permanent delete isn't possible through this API, so the UI doesn't offer it. If the API gets fixed, `UpstreamApi:UseDeleteEndpoint=true` switches back to DELETE (with `Connection: close`, falling back to deactivation if the body is dropped again). |
| Error responses include stack traces and SQL details | Upstream 5xx bodies are logged and replaced with a generic `502`. |
| The test account and its data are shared by all candidates (records such as `dika` or `LIVE-TEST-2007`) | Validation accepts the existing data (lowercase codes, empty parent codes), and the UI copes with mixed icon names. |

Other notes:
- `createdBy` / `updatedBy` / `deletedBy` are the logged-in username.
- There is no partner endpoint (requests carry only `partnerId`). See the frontend README for how company names are shown.
