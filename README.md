# World News Alerts

ASP.NET Core 10, Angular 22, SQLite/EF Core and Identity. Implements the workflows in `development-plan.md` and `Implementation Plan.md`, including Codex retrieval, persistent immediate delivery, SMTP **and Resend**, public Slack channels, Slack OAuth, and administration.

## Run locally

Install the .NET 10 SDK and Node.js 24 LTS. In two terminals from the repository root:

```powershell
dotnet tool restore
dotnet restore
dotnet run --project backend/src/WorldNewsAlerts.Api
```

```powershell
cd frontend
npm ci
npm start
```

Open **http://localhost:4200**. Backend: http://localhost:5080; development OpenAPI: http://localhost:5080/openapi/v1.json.

The initial EF migration applies automatically. Development uses clearly labeled fictional news fixtures and saves email HTML/text to `backend/src/WorldNewsAlerts.Api/App_Data/mail`. Registration also displays a development-only verification link. Open it, verify, sign in and save subscription preferences. Two separate emails are queued immediately: confirmation and initial digest. Production never exposes the verification link in the registration response and never permits the development email provider.

There are no default accounts or passwords. To provision an administrator, set environment variables **before starting** the backend:

```powershell
$env:Admin__Email = 'your-admin@example.com'
$env:Admin__Password = '<your-strong-password>'
dotnet run --project backend/src/WorldNewsAlerts.Api
```

The configured administrator is email-confirmed and receives the Admin role. Remove the bootstrap password from the environment after provisioning. A normal user cannot access the administration API or routes.

## Email: Resend or SMTP

Select **Resend**, **SMTP**, or the local development inbox in **Administration → Delivery channels**. Provider selection is saved in SQLite and used by account emails and news alerts. Configure credentials through environment variables or an ignored `backend/src/WorldNewsAlerts.Api/appsettings.Local.json` file. Never put credentials in tracked files.

For [Resend's send-email API](https://resend.com/docs/api-reference/emails/send-email):

```powershell
$env:Runtime__EmailProvider = 'resend'
$env:Email__FromAddress = 'World News Alerts <news@your-verified-domain.example>'
$env:Email__Resend__ApiKey = '<your-resend-api-key>'
```

Use a sender/domain authorized by your Resend account and observe its recipient restrictions. Requests include HTML, plain text and a stable `Idempotency-Key` derived from the persistent delivery job. Resend retains those keys for 24 hours; the local delivery ledger persists independently.

For SMTP:

```powershell
$env:Runtime__EmailProvider = 'smtp'
$env:Email__FromAddress = 'news@your-domain.example'
$env:Email__Smtp__Host = 'smtp.your-provider.example'
$env:Email__Smtp__Port = '587'
$env:Email__Smtp__Security = 'StartTls'
$env:Email__Smtp__Username = '<username>'
$env:Email__Smtp__Password = '<password>'
```

SMTP uses MailKit. `Security` supports MailKit's `SecureSocketOptions` values, including `StartTls` and `SslOnConnect`. Use `None` only for a local mail capture server. Admins can queue a test email to their own verified address and inspect its status under Jobs.

**Configuration precedence:** admin runtime settings persisted in SQLite override `Runtime__...` startup defaults. After saving settings in the admin UI, change them there. Secrets always come from server configuration, never SQLite runtime settings or API responses.

## Codex news retrieval

Development defaults to `fake`; select **Codex CLI** under Administration → News providers when ready. Install and authenticate the CLI as the backend service account. Configure `Codex__Executable` to the **native executable**, especially on Windows where an npm `codex.cmd`/PowerShell shim cannot be started without a shell. A full path to `codex.exe` is supported. `Codex__WorkingDirectory` controls its dedicated scratch parent, and `Codex__TimeoutSeconds` defaults to 180.

The adapter uses `ProcessStartInfo.ArgumentList`, stdin prompts, a read-only sandbox, live web search, timeout/cancellation, schema validation and a dedicated GUID scratch directory per run. It uses [`codex exec --output-schema --output-last-message`](https://learn.chatgpt.com/docs/non-interactive-mode) and parses the final JSON file, not the JSONL execution log. The query embeds the expected JSON contract. CLI diagnostics are drained without persisting potentially sensitive output.

Run unattended CLI authentication and live-search checks under that account before enabling scheduled retrieval. The adapter validates source URL structure and timestamps; it cannot independently prove that a linked publisher supports every generated claim. Review original sources when assessing results. No external Codex invocation is made by automated tests.

Retrieval runs hourly by default, with overlapping windows and recovery from the last successful run, capped at seven days. Changing its interval in administration takes effect during subsequent worker checks. Source URL normalization, unique source/fingerprint indexes, and conservative title/date/location matching prevent common duplicate events. Semantic matching of differently worded stories remains heuristic.

## Slack

Create a Slack app and configure these bot scopes: `chat:write`, `channels:read`, `groups:read`, `im:write`. Invite the bot to channels where it should post.

For user-connected workspaces, configure:

```powershell
$env:Slack__ClientId = '<client-id>'
$env:Slack__ClientSecret = '<client-secret>'
$env:Slack__RedirectUri = 'http://localhost:4200/api/slack/callback'
```

Register that exact redirect URL with your Slack app. In deployment use its HTTPS equivalent. Users connect from Slack workspaces, authorize the app, optionally add a bot-authorized channel, then enable the destination in Subscriptions. The OAuth installer member ID is taken from Slack's token-exchange response, not a typed user ID. State is user-bound, cookie-correlated, expires after ten minutes and is consumed once. Tokens are protected using ASP.NET Data Protection. Disconnect removes that user's subscription links; it does not uninstall the shared app for other users. Revoke the installation in Slack to uninstall it entirely. See [Slack's OAuth v2 flow](https://docs.slack.dev/authentication/installing-with-oauth/).

For your public workspace, additionally configure `Slack__Public__TeamId`, `Slack__Public__BotToken`, `Slack__Public__Name`, `Slack__Public__InviteUrl`, and channel IDs such as `Slack__Public__Channels__health`, `Slack__Public__Channels__geopolitics`, and optionally `Slack__Public__Channels__exceptional`. Restart to seed public destinations. Give each category its own channel. Public channels use their configured category feed and deliver globally once per event/revision; joining does not link an application account. Direct messages use personal subscription filters.

## Persistence and delivery

The system targets **one backend instance** and a persistent local SQLite file. WAL mode and a connection busy timeout are enabled. All dates are UTC. The database is the source of truth; an in-process signal wakes dispatch immediately and a default 30-second scan recovers missed signals. Processing leases recover abandoned deliveries. Bounded exponential retries reuse the same job. Admin retry resets the failed job rather than creating another.

News and its matching delivery jobs commit together. Initial confirmation/digest and subscription preferences also commit together. A unique SQLite index on `(NewsEventId, DestinationId, EventRevision)` enforces destination-based deduplication. Digest items populate that ledger. Revision alerts require explicit subscription opt-in. Disabling or deleting a subscription cancels queued private emails; shared public channel feeds continue for their members. A message already accepted externally can be repeated if the process crashes before recording success; SMTP and Slack do not have an atomic transaction with SQLite.

Persist and back up **both the database and `App_Data/keys`**. Losing the Data Protection key ring invalidates protected Slack tokens, cookies and unsubscribe links. Restrict filesystem access to the backend account. Do not run multiple backend replicas against the same SQLite file.

## API and checks

API routes are available at `/api` and the versioned alias `/api/v1`. News/catalog are public; subscriptions, destinations, profile and Slack require a verified account; `/admin/*` requires Admin. Mutations require a CSRF token from `GET /api/auth/csrf`, supplied in `X-XSRF-TOKEN` along with cookies. Angular handles this automatically. Cookies are HttpOnly, SameSite and HTTPS-only in production. Authentication endpoints have rate limits and login lockout. See [ASP.NET antiforgery documentation](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

```powershell
dotnet test WorldNewsAlerts.slnx
cd frontend
npm test
npm run build
```

Backend tests exercise real SQLite migrations, registration/verification/cookie authentication/CSRF, admin authorization, filtering, initial emails, digest deduplication, shared Slack deduplication, retries, unsubscribe/deletion, Codex validation and mocked Resend HTTP delivery. Angular tests cover CSRF/session handling and subscription forms. No real messages are sent.

With both development servers running and Microsoft Edge installed, `npm run test:e2e` in `frontend` checks the full reader flow and desktop/mobile layouts using Playwright. It creates and deletes a temporary example.com account and captures screenshots under the ignored `frontend/test-results` directory. Set `E2E_BROWSER=chromium` (with Playwright Chromium installed) to use Chromium instead, or `E2E_BASE_URL` to target another development URL. See [Angular testing guidance](https://angular.dev/guide/testing) for the unit-test runner.

## Deployment

Set `App__PublicUrl` and `Slack__RedirectUri` to your public HTTPS site. Configure Resend or SMTP and native Codex authentication. Use a patched .NET 10 runtime. Run `./scripts/publish.ps1` to build Angular into the backend's `wwwroot` and publish to `artifacts/publish`. Run the published API from that directory with `ASPNETCORE_ENVIRONMENT=Production`, behind HTTPS, using persistent storage for `App_Data` (or an absolute SQLite connection path and a persistent key ring). `/health` provides process liveness. Migrations run on startup; back up before upgrades. The Angular proxy is only needed during separate frontend development.

Live Resend, SMTP, Slack installation/delivery and unattended Codex retrieval require your credentials and service configuration. They are implemented but are not exercised against real external accounts by this repository's tests.
