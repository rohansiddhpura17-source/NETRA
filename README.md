# NETRA

NETRA is a crime analytics and case management web application built with **ASP.NET Core MVC** (controllers + Razor views), **EF Core**, and **SQL Server**. Written entirely in C#.

## Architecture

Five-project clean architecture. Dependencies point inward only:

```
NETRA.Domain          (entities, enums - no external infrastructure deps)
      ↑
NETRA.Application     (application services, interfaces)
      ↑
NETRA.Infrastructure  (EF Core DbContext, Identity, seeding)
      ↑
NETRA.Web             (ASP.NET Core MVC controllers + Razor views)
```

- `NETRA.Domain` must not reference `Infrastructure` or `Web`.
- `NETRA.Application` must not reference `Web`.
- `NETRA.Infrastructure` must not reference `Web`.
- `NETRA.Tests` references the others for verification only.

Presentation flow:

```
HTTP request → Controller → Application Service → DbContext → Razor View
```

## Prerequisites

- .NET SDK 10.0
- SQL Server (local instance or remote), reachable via the configured connection string
- EF Core CLI tool (optional, for manual migration commands):
  ```bash
  dotnet tool install --global dotnet-ef
  ```

## Configuration

### Connection string

The committed baseline in `Web/appsettings.json` is a credential-free placeholder for
Windows integrated authentication on the local machine, and it does **not** trust server
certificates:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=NETRA;Trusted_Connection=True;MultipleActiveResultSets=true"
}
```

`Web/appsettings.Development.json` adds `TrustServerCertificate=True` because a local
developer certificate is self-signed. That exception is confined to Development: a
deployment that inherits the shipped baseline validates the server certificate, so TLS
cannot be silently downgraded by editing a tracked file.

No database credentials are ever committed. Supply the real value through one of these,
in the order the configuration system already resolves them:

```bash
# 1. Environment variable (works in every environment, including containers)
export ConnectionStrings__DefaultConnection="Server=tcp:...;Database=NETRA;User Id=...;Password=...;Encrypt=True"

# 2. User secrets (Development only; stored outside the repository)
dotnet user-secrets --project Web set "ConnectionStrings:DefaultConnection" "Server=...;..."
```

`Web/NETRA.Web.csproj` carries `UserSecretsId=NETRA.Web`, so the second path works out of
the box and the secret store lives in the user profile, never in the working tree. Phase 15
verified the precedence by pointing the secret store at a non-existent host and watching
startup fail on that host's DNS lookup.

### Required environment variables

| Variable | Purpose | Default if unset |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Selects the config and security profile | `Production` |
| `ConnectionStrings__DefaultConnection` | SQL Server endpoint and credentials | local integrated auth placeholder (fails startup) |
| `Database__AutoMigrate` | Apply migrations on startup | `true` in Development, `false` otherwise |
| `Ai__Provider` | `Mock` or `Local` | `Mock` |
| `Ai__Endpoint`, `Ai__Model`, `Ai__TimeoutSeconds` | Local model server | `http://localhost:11434`, `llama3.2`, `15` |
| `NETRA_DEV_ADMIN_PASSWORD` | Development admin bootstrap password | no admin account is created |
| `ASPNETCORE_URLS` | Bind addresses | `launchSettings.json` in Development |

`FileStorage__RootPath` may override the evidence store location
(default `App_Data/evidence` under the content root).

### AI provider settings

`Web/appsettings.json` carries the non-secret AI defaults (see
[NETRA AI Assistant](#netra-ai-assistant-phase-9)):

```json
"Ai": { "Provider": "Mock", "Endpoint": "http://localhost:11434", "Model": "llama3.2", "TimeoutSeconds": 15 }
```

Override with environment variables (`Ai__Provider`, `Ai__Endpoint`, `Ai__Model`,
`Ai__TimeoutSeconds`). The assistant requires no API keys; never commit secrets or
endpoints that embed credentials.

## UI

NETRA is **ASP.NET Core MVC** with Razor views. There are no Razor Pages. Phase 4
verification confirms there are no JavaScript or TypeScript application files, no
React, Angular or Vue frontend, no Node.js or Python backend, no `<script>` blocks for
application functionality, and no inline JavaScript handlers. JSON configuration
files are expected and allowed. Interactive behaviour is server-rendered HTML and CSS
only (for example the narrow-viewport navigation drawer uses a hidden checkbox).

### Design system

All styling lives in one stylesheet, `Web/wwwroot/css/netra.css`, built on CSS custom
properties.

The Phase 3 visual baseline used a `224px` sidebar and a `52px` topbar. During the
Phase 4 fidelity review these metrics were intentionally changed after direct visual
comparison with the supplied `netra.pdf`: the sidebar now uses an approximately
`248px` responsive baseline (`236px` at the 1440px breakpoint), and the topbar is
`56px` high. These are deliberate fidelity corrections, not unchanged Phase 3 values.

Design tokens:

| Token | Value | Use |
|---|---|---|
| `--netra-background` | `#081326` | page, topbar |
| `--netra-surface` | `#101B2E` | sidebar, cards |
| `--netra-surface-elevated` | `#1B2639` | active nav, raised rows |
| `--netra-surface-sunken` | `#0A1424` | inputs, table headers |
| `--netra-border` | `#1E2C42` | hairlines |
| `--netra-primary` | `#6DE2FA` | primary buttons, active states |
| `--netra-text` / `--netra-text-muted` | `#E6EEF7` / `#94A3B8` | text hierarchy |
| `--netra-success` `--netra-warning` `--netra-danger` `--netra-info` | — | status |

### Shared components

Reusable Razor partials in `Web/Views/Shared`:

`_Layout`, `_AuthLayout`, `_Sidebar`, `_Topbar`, `_PageHeader`, `_Breadcrumbs`,
`_StatusBadge`, `_StatCard`, `_FilterPanel`, `_DataTable`, `_EmptyState`,
`_Pagination`, `_FormSection`, `Error`

Icons are inline SVG rendered server-side (`NetraIconPaths`), so there is no icon font
or CDN dependency.

### Modules not yet built

Sidebar entries for modules scheduled for later phases resolve to
`PlaceholderController`, which renders an explicit "not yet implemented" page. No
placeholder shows fabricated data.

## Case module (Phase 4)

The case module covers crime case registration, the filterable case list, the case
record with its associations, and the investigation workspace. Cases are created
through the UI with an initial investigation; case numbers are generated server-side
as `CC-<year>-####` when left blank. Every mutating case operation writes an audit
entry, and evidence uploads are stored on disk under the configured `FileStorage`
directory rather than in the database.

### Routes

| Route | Verb | Policy | Purpose |
|---|---|---|---|
| `/Cases` | GET | `CaseRead` | Filterable, paginated case list |
| `/Cases/Create` | GET, POST | `CaseCreate` | Register a case and its initial investigation |
| `/Cases/Details/{id}` | GET | `CaseRead` | Case record, associations, change history |
| `/Cases/Edit/{id}` | GET, POST | `CaseEdit` | Edit the case record |
| `/Cases/Details/{id}/Persons` | POST | `CaseManagePersons` | Associate a person with a role |
| `/Cases/Details/{id}/Persons/{casePersonId}/Remove` | POST | `CaseManagePersons` | Remove a person association |
| `/Cases/Details/{id}/Locations` | POST | `CaseManageLocations` | Associate a scene/related location |
| `/Cases/Details/{id}/Locations/{caseLocationId}/Remove` | POST | `CaseManageLocations` | Remove a location association |
| `/Cases/Details/{id}/Evidence` | POST | `CaseManageEvidence` | Record evidence, optional file attachment |
| `/Cases/Details/{id}/Notes` | POST | `CaseManageNotes` | Add an investigation note |
| `/Cases/Details/{id}/Timeline` | POST | `CaseManageTimeline` | Add a timeline event |
| `/Cases/Details/{id}/Assign` | POST | `CaseAssign` | Assign or clear the lead investigator |
| `/Investigations/Details/{id}` | GET | `CaseRead` | Investigation workspace; accepts an investigation id or a case id |

Policies are declared in `NetraPolicies` and map to the seeded roles via
`NetraRoles`; forbidden requests return a real `403` rendered by the styled error
view. All case writes go through `ICaseService` in `NETRA.Application`, so
controllers never touch EF Core directly.

### Known SQL Server limitation

The machine this project is developed on has **no SQL Server instance** (no Docker,
nothing listening on port 1433). Consequently:

- Live SQL Server CRUD has **not** been exercised in this environment.
- Authenticated UI behaviour is verified by the integration test suite, which boots
  the real application with `WebApplicationFactory` and swaps the EF Core provider to
  in-memory with `Database:AutoMigrate=false`.
- Startup, redirect and error-page behaviour without a database is verified by
  running the app with `Database:AutoMigrate=false` (see the smoke-test notes under
  *Database setup*).
- To verify against a real server, point `ConnectionStrings__DefaultConnection` at
  it and start in Development (which migrates and seeds), or apply migrations
  manually with `dotnet ef database update`.

## Intelligence Dashboard and Crime Analytics (Phase 5)

Both screens are fully database-backed. Every figure is produced by EF Core queries
with server-side aggregation and DTO projection (`AsNoTracking`, async, cancellation
tokens); nothing is hard-coded, randomised or loaded into memory just to be counted.

### Routes

| Route | Verb | Policy | Purpose |
|---|---|---|---|
| `/Dashboard` | GET | `CaseRead` | Intelligence Dashboard: KPI cards, 12-month crime trend, type distribution, district comparison, high-risk areas, recent alerts |
| `/Analytics` | GET | `AnalyticsRead` | Crime Analytics: filtered report with volume metrics, trend, type/district/severity breakdowns, day-of-week and time-of-day patterns, deterministic insights |

`AnalyticsRead` requires the System Administrator, Senior Officer or
Police / Intelligence Analyst role. The dashboard requires an authenticated case
worker. Sidebar visibility mirrors these policies but is presentation only — the
attributes enforce access independently.

### Services and data flow

```
Controller → IDashboardService / IAnalyticsService → EF Core aggregates → DTO → ViewModel → Razor view
```

- `DashboardService` (`NETRA.Infrastructure`) computes total/active case and active
  alert counts with targeted `CountAsync` queries, ranks high-risk districts from
  recorded `RiskAssessment` rows (risk level ≥ 3 — an *analytical indicator* derived
  from stored assessments, never an invented score), and selects the five most recent
  unresolved alerts. Trend, distribution and district comparison reuse
  `IAnalyticsService` so aggregation logic exists once.
- `AnalyticsService` translates the GET filters (date range, district, crime type,
  severity) into composable `IQueryable` predicates and performs grouping in the
  database. The default window is the trailing 12 months.
- Views receive view models only; EF entities never reach the Razor layer.

### Server-side SVG charts

Charts are rendered as accessible SVG markup by C# renderers in
`Web/Services/Charts` (`SvgLineChartRenderer`, `SvgBarChartRenderer`,
`SvgDonutChartRenderer` behind `IChartRenderer`, resolved per kind by
`ChartRendererRegistry`). Each chart carries `role="img"`, a unique
`aria-labelledby` title and description, axis labels, per-point/per-segment `<title>`
tooltips and a legend, so the data is understandable without graphics. There is no
chart JavaScript of any kind.

### Filters and empty states

The analytics filter band is a plain GET form (Date from, Date to, District, Crime
type, Severity) with **Apply Filters** / **Clear filters**; submitted values are
preserved on redisplay and an inverted date range shows a validation message instead
of failing. When a period contains no records, charts and panels fall back to
professional empty states (e.g. "No crime records are available for the selected
period.") rather than fabricated zeroes presented as insight.

### Testing status for Phase 5

Dashboard and analytics services, chart renderers, filters, authorization and both
pages' rendering are covered by xUnit tests against the in-memory provider (see
`Tests/Application/AnalyticsServiceTests.cs`, `Tests/Application/DashboardServiceTests.cs`,
`Tests/Web/ChartRendererTests.cs`, `Tests/Web/DashboardAnalyticsIntegrationTests.cs`).
The queries use standard EF Core LINQ that translates to SQL Server, but — as noted
above — this machine has no SQL Server instance, so the aggregations have **not**
been executed against a live SQL Server; only the in-memory provider was exercised.

## Risk & Anomaly Analysis and Crime Intelligence Map (Phase 6)

Both screens are analytical **decision support**, never prediction: risk scores are
*analytical indicators* computed from recorded case data and do not represent
guaranteed predictions of future crime, and no output ever assigns guilt to a person.
The same inputs always produce the same outputs — there is no randomness anywhere in
either module.

### Routes

| Route | Verb | Policy | Purpose |
|---|---|---|---|
| `/Risk` | GET | `RiskRead` | Risk & Anomaly Analysis: area indicator table, weekly sparklines, historical trend, detected anomalies |
| `/Risk/Assess` | POST | `RiskRead` + antiforgery | Recomputes indicators and persists area assessments and anomaly records |
| `/Map` | GET | `CaseRead` | Crime Intelligence Map: server-rendered SVG of recorded incident coordinates with case quick view |

`RiskRead` requires the System Administrator, Senior Officer or
Police / Intelligence Analyst role; the map reuses the case-read policy. Mutations
happen only on the POST assessment route (antiforgery-validated, audited) — GET
requests never persist anything.

### Risk scoring methodology (deterministic and explainable)

Implemented in `RiskAnalysisService` (`NETRA.Infrastructure`); the formula is
documented in the class XML doc and mirrored here. For each district, the selected
window (default: trailing 30 days) is compared with the immediately preceding period
of equal length:

| Component | Formula | Cap |
|---|---|---|
| Volume | `cases × 4` | 40 |
| Severity | `high × 3 + critical × 6` | 35 |
| Change | growth vs comparison period, `100% → 15` linear, capped | 15 |
| Concentration | leading crime-type share `× 10` | 10 |

`Score = clamp(0, 100, sum)` (1 decimal). Levels: ≥80 Critical, ≥60 High, ≥40
Elevated, ≥20 Moderate, else Low. A district with fewer than **3 cases** across both
periods is reported as *Insufficient data* with no score — the system never invents a
number for thin data. Every indicator stores and displays a full explanation:
district, window, comparison period, counts, deviation and the per-component
contribution ("5 cases in the window versus 2 in the comparison period (+150% …);
2 high and 1 critical severity. Components: volume +20, …").

### Anomaly detection (comparative rules only)

`AnomalyDetector` (Application layer) raises an anomaly **only when a window deviates
from its own comparison period** beyond documented thresholds — a high absolute value
alone never triggers one:

- **Volume spike** — ≥4 cases, ≥2 more than the comparison period and ≥1.5× (critical
  at ≥2×; a spike from zero baseline qualifies).
- **Severity shift** — high/critical cases at least double and gain ≥2 absolute.
- **Crime concentration** — ≥5 cases with one type holding ≥60%.
- **Recurring pattern** — a leading type present in ≥3 of the window's weekly
  buckets (≥60% active weeks) with ≥3 cases.

Each anomaly carries an explanation, a confidence derived from the comparison sample
(never a fabricated certainty) and the affected area. `RunAssessmentAsync` upserts
area assessments in place, de-duplicates open anomalies by
(district, type, title), and writes audit entries for every persisted record.

### Crime Intelligence Map (server-rendered, no GIS library)

The map is **not** an interactive GIS system and says so on screen. There is no
Leaflet, Mapbox, Google Maps, OpenLayers or any other JavaScript map library — the
visual is an SVG generated entirely server-side by `SvgMapRenderer` (`Web/Services/Maps`)
from `MapService` data:

- `MapService` selects incident-location rows for the filtered window, validates
  coordinates (latitude in [−90, 90], longitude in [−180, 180], not (0,0)), and
  **reports** records without usable coordinates instead of crashing or silently
  dropping them — one bad record never breaks the map. At most 400 incidents are
  plotted (newest first).
- Points are projected equirectangularly into a padded bounding box; an 8×8 grid
  cluster of ≥3 incidents becomes a hotspot whose radius scales with density.
- Case quick view is a plain server round-trip (`/Map?caseId=…` renders a Selected
  Incident card); there are no JavaScript popups. Filters (district, date range,
  crime type, severity) are GET-preserved across every link.

### Data flow and performance

```
Controller → IRiskAnalysisService / IMapService → filtered EF Core projection (AsNoTracking, Distinct, bounded rows)
           → deterministic in-memory grouping → DTO → ViewModel → Razor view (+ server-side SVG renderer)
```

Filtering, projection and row de-duplication are translated to SQL; only the bounded
set of distinct case rows inside the window is materialised. Weekly bucketing and
composition arithmetic run over those rows (record constructors inside translatable
projections are not portable across EF providers, and `DateTime` subtraction
bucketing does not translate on SQL Server, so both are deliberately client-side over
the bounded row set).

### Testing status for Phase 6

Risk scoring (band-by-band hand-computed expectations), determinism, insufficient
data, comparison windows, anomaly rules and explanations, coordinate validation,
filtering, SVG geometry and encoding, empty states, the POST-only assessment run and
the authorization matrix are covered by xUnit tests
(`Tests/Application/AnomalyDetectorTests.cs`, `Tests/Application/RiskAnalysisServiceTests.cs`,
`Tests/Application/MapServiceTests.cs`, `Tests/Web/MapRendererTests.cs`,
`Tests/Web/RiskMapIntegrationTests.cs`). As with earlier phases, the queries are
standard EF Core LINQ but have **not** been executed against a live SQL Server on
this machine — only the in-memory provider was exercised.

## Network & Link Analysis (Phase 7)

/Network provides a bounded investigation view over relationships already stored in
NETRA. It does not create or depend on a separate graph database.

| Route | Verb | Policy | Purpose |
|---|---|---|
| /Network | GET | NetworkRead | Explore recorded person, case, and location relationships |

### Data model and traversal

The network service projects these existing relational associations:

- CasePerson (Person → Case), using the recorded role to map Suspect, Victim and
  Witness links to the existing RelationshipType enum;
- CaseLocation (Case → Location);
- PersonLocation (Person → Location);
- active NetworkRelationship rows, whose endpoints resolve through
  NetworkEntity to exactly one real Person, CrimeCase or Location.

NetworkEntity has foreign keys to the real records and is now additionally protected
by the CK_NetworkEntities_ExactlyOneReference SQL constraint. A reference wrapper
with no target or multiple targets cannot be persisted. The renderer uses the real
record’s current display data; it does not duplicate persons, cases or locations.

NetworkAnalysisService uses deterministic breadth-first traversal. The selected
record begins at depth 0; each newly discovered layer is queried in a batch, ordered
by stable entity keys, and added only once to a visited set. Depth is configurable
from 1 to 3 (default 2), so cycles cannot recurse forever. The result is also capped
at 120 nodes, 240 links, and 320 relationship candidates per layer. All reads are
AsNoTracking, projected to DTOs, asynchronously executed, and filtered before the
bounded rows are materialised.

### Filters, metrics, and rendering

The page uses a plain GET form:

- starting type and entity (Person, Case, Location), with a bounded directory search;
- existing RelationshipType values;
- traversal depth;
- Start and End dates.

Date filtering happens in the service, not in the browser. A link is kept only when
its own stored date (association date, person-location start, or relationship
established date, with creation date as fallback) falls inside the selected period;
case links additionally require the stored incident date to be inside it. Selected
values remain in the URL and in the rendered controls.

Metrics are descriptive only: number of connections, connected persons/cases/locations
(records that participate in at least one displayed link), and each visible node's
degree (the count of displayed links incident to that node).
They are deterministic counts, not risk, guilt, or centrality conclusions.

INetworkGraphRenderer generates accessible SVG on the server. The selected entity
is centred; hop layers use evenly spaced concentric elliptical rings ordered by
type/name/key, with each ring rotated a quarter turn per hop so sparse layers never
stack on top of each other. Identical graph data always produces identical geometry.
SVG includes node
type shapes, link and node labels, a legend, selected-node styling, and a title and
description. Network visualization is rendered server-side as SVG. Graph interaction
occurs through server round-trips rather than client-side JavaScript.

### Authorization and privacy

NetworkRead is explicit and allows System Administrator, Senior Officer, and
Police / Intelligence Analyst. Investigator is deliberately denied (403), while an
anonymous request is redirected to login. Those roles are already authorised case
readers; NETRA currently has role-based case access rather than a separate row-level
ACL. The service exposes only active person/location records, current case display
metadata, and relationships reachable from the selected record within the requested
bounds. It deliberately omits national IDs, contact information, addresses, notes,
evidence, and free-text relationship evidence from network DTOs and the SVG.

The Phase 7 tests cover entity lookup, depth, cycles, relationship/date filters,
metrics, empty and not-found states, integrity configuration, SVG determinism and
encoding, the authorization matrix, filter persistence, and the no-JavaScript page
audit. They run with EF Core InMemory through the application test host; they are not
live SQL Server tests.

## Image-Assisted Investigation (Phase 8)

Phase 8 adds image-assisted investigation: an authorised user uploads an image that is
linked to an active case, the server stores it and asks a match provider for
ranked candidates, and every result is presented strictly as an **investigative lead**.

| Route | Purpose |
|---|---|
| `/ImageInvestigation` | Upload form, analysis record, subject preview, candidate grid |
| `/ImageInvestigation/Match/{id}` | Candidate match detail: associated cases and locations |
| `/ImageInvestigation/Image/{id}` | Authorized inline preview of a stored image |

Architecture: `ImageInvestigationController` → `IImageInvestigationService` →
`ImageInvestigationService` → `IFileStorageService` + `MockImageMatchProvider` → DTOs →
ViewModels → Razor. No new CSS system; pages reuse the `netra-*` design tokens.

### Upload validation

All validation is server-side and runs before any bytes are stored:

- Filename must be non-empty and contain no `/`, `\`, or `..` sequences.
- Extension must be `.jpg`, `.jpeg`, `.png`, or `.webp`, and the content type must be
  `image/jpeg`, `image/png`, or `image/webp`.
- Size must be greater than zero and at most 10 MB (`ImageInvestigationLimits.MaxUploadBytes`).
- Magic bytes must actually match the declared format (JPEG, PNG, or RIFF/WEBP);
  renamed or malformed files are rejected.
- The selected case must exist and not be Closed or Archived.
- A duplicate checksum is rejected before storage with an honest message.

### Secure storage model

Uploads go through `FileStorageService` under the configured `FileStorage:RootPath`.
Storage names are server-generated (`{guid:N}{ext}` under the case-number folder);
the original filename is kept only as a display label and never used as a path.
Physical paths are never exposed to the UI, downloads are addressed only by record id
through an authorized controller action, and the preview endpoint checks authorization
before returning any image bytes.

### Mock match provider and determinism

`MockImageMatchProvider` is a clearly synthetic, deterministic demo provider: the same
source/candidate pair always produces the same similarity score, computed as
`SHA256("{source}:{candidate}")` mapped into [0.40, 0.95]. Candidates are drawn from
images already stored in NETRA (excluding the source itself) and are labelled
`DEMO-CAND-nn`. It connects to nothing: no real police, criminal, or biometric
databases and no external identity data are involved. If the provider throws, the
result degrades to `ProviderAvailable = false` rather than an exception page.

### Honest limitations

The current implementation is a controlled academic/demo feature. Similarity results
are investigative leads and do not establish identity or guilt. The UI states
"Investigative Lead — Verification Required" on every result page, labels scores as
synthetic, and never presents a match as a confirmed person. Because candidates come
from NETRA's own stored images, a fresh database shows the honest empty state until at
least two images exist; nothing is fabricated.

### Authorization and audit

Access is governed by `NetraPolicies.ImageInvestigation`: System Administrator,
Investigator, and Police / Intelligence Analyst are allowed; Senior Officer receives
403 and anonymous users are redirected to sign-in. Sidebar visibility is not treated as
security — every action enforces the policy server-side. Each operation writes audit
entries via the centralized `AuditService` (`EvidenceUploaded` on upload,
`ImageAnalysisRequested`, `ImageResultsViewed`, `ImageMatchViewed`); image bytes are
never written into the audit log.

### Resource limits and retention

Candidate lists are capped at 4 (`MaxCandidates`), the case selector at 200 active
cases (`MaxCaseOptions`), and traversal is bounded; there are no unbounded loops.
Stored images persist until explicitly deleted — this demo build has no archival or
purge system, and none was invented.

### No JavaScript and database caveat

The module is pure server-rendered Razor: no JavaScript files, no `<script>` blocks,
no inline handlers, and the upload uses a native form post. Phase 8 tests (service
unit and web integration) verify validation, storage safety, determinism,
authorization, audit behaviour, and the no-JavaScript guarantee with EF Core InMemory
through the application test host; they are not live SQL Server tests.

## NETRA AI Assistant (Phase 9)

Phase 9 adds a server-rendered AI assistant at `/Assistant`: an authorised user asks a
natural-language question about crime data, the server classifies it into one of a fixed
set of intents, runs **one approved query operation** through the existing application
services, and an AI provider phrases the authorized facts into an answer. The AI is a
response-generation component, never an authorization layer, and the application works
fully offline with no external AI dependency.

| Route | Purpose |
|---|---|
| `/Assistant` | Conversations list, chat transcript, question form (`?session={id}` to reopen one) |
| `/Assistant/Ask` | POST a question (anti-forgery protected) |
| `/Assistant/New` | POST to start a fresh conversation |

### Architecture

`AssistantController` → `IAssistantService` → `AssistantService` →
`AssistantIntentResolver` (pure, deterministic) → approved query operations via the
existing `ICaseService`, `IAnalyticsService`, `IRiskAnalysisService` (plus one bounded
person-association projection) → minimized fact summary → `IAiService` → persisted
`AssistantMessage` pair + audit. No SQL is ever generated from user text; the resolver
only chooses *which* pre-written query operation runs with what parameters.

### Supported intents

`CaseSearch`, `CrimeTypeCount`, `DistrictComparison`, `PersonCaseLookup`,
`RecentCrimeTrend`, `AnomalySummary`, `CaseDetails` (case-number pattern), and `Unknown`.
Unknown or unsafe questions receive fixed safe guidance without any query or provider
call. Each answered question states its source: the record count retrieved by the
approved query layer and the time window used.

### Data minimization

The AI provider receives only short fact summaries built from DTOs: case numbers, titles,
crime-type and district names, dates, statuses, counts and scores. National IDs,
passport/license numbers, phone numbers, emails, private addresses, dates of birth,
evidence contents, notes, passwords, security settings and audit internals are never
passed to a provider or rendered in answers. Result rows are capped
(`AssistantLimits.MaxResultRows = 10`), windows are clamped (1–3650 days, default 30),
and "show all records"-style questions return bounded summaries, never dumps.

### Providers: Mock and Local, with fallback

| `Ai:Provider` | Behaviour |
|---|---|
| `Mock` (default) | `MockAiService` — deterministic offline formatting of the authorized facts with decision-support wording; demonstrates the real workflow end to end |
| `Local` | `LocalAiService` — Ollama-compatible `/api/generate` HTTP call via a typed `HttpClient`; endpoint, model and timeout come from `Ai:*` configuration (override with `Ai__Provider`, `Ai__Endpoint`, `Ai__Model`, `Ai__TimeoutSeconds` env vars). Never hard-code keys or secret-bearing endpoints; the assistant needs no API key |

If the configured provider fails, times out or is unreachable, the failure is audited and
the same authorized facts are formatted by `MockAiService` as an explicit fallback —
the assistant never shows an exception page and never invents an answer.

### Sessions and authorization

Conversations use the existing `AssistantSession`/`AssistantMessage` entities through EF
Core — no second database. Every read and write is scoped to the authenticated user;
another user's session id behaves as "not found" and the attempt is audited. Access is
governed by `NetraPolicies.Assistant` (System Administrator, Senior Officer,
Police / Intelligence Analyst); Investigator receives 403, matching the analytics/risk
reader group because the assistant exposes those datasets. Sidebar filtering is not
treated as security — the policy is enforced server-side on every action.

### Prompt-injection defenses and response safety

Questions are untrusted input. A blocked-phrase layer (e.g. "ignore all restrictions",
"database password", "execute", "SELECT *", "dump") routes straight to the fixed
guidance answer with zero queries. The deterministic resolver cannot be steered into
arbitrary SQL, and the LocalAiService prompt instructs the model to answer only from
the supplied facts and to ignore instructions embedded in the question. Answers use
lead wording ("Based on the available NETRA records…", "Analytical indication…"),
honest empty wording ("No matching NETRA records were found…"), and never assert
identity, guilt or certainty.

### Audit

Every step writes to the central audit trail via `AuditService`:
`AssistantSessionCreated`, `AssistantQuestionSubmitted`, `AssistantQueryExecuted`
(intent + record count), `AssistantResponseGenerated`, `AssistantProviderFailure`,
and `AssistantAccessDenied`. No provider credentials or raw data payloads are stored
in audit entries.

### No JavaScript and database caveat

The module is pure server-rendered Razor with strongly typed ViewModels and the
existing `netra-*` CSS system; no `<script>`, handlers or `javascript:` URLs. Phase 9
tests (intent resolver, service, LocalAiService and web integration) run against EF
Core InMemory through the application test host; they are not live SQL Server tests.

## Alerts & Notifications (Phase 10)

Phase 10 turns the analytical findings of Phases 5–9 into a triageable alert feed at
`/Alerts` and an internal notification centre at `/Notifications`. Alerts are records of
what NETRA already measured — risk indicators, anomaly candidates, recorded case
associations and audit-trail security events — surfaced with their analytical basis
intact. Nothing is inferred, and no metric on either page is faked.

| Route | Purpose |
|---|---|
| `/Alerts` | Alert feed with summary cards, server-side GET filters and pagination |
| `/Alerts/Details/{id}` | One alert: facts, analysis window, analytical basis, triage actions |
| `POST /Alerts/Generate` | Explicit, idempotent generation pass (`windowDays`, default 30) |
| `POST /Alerts/Acknowledge\|Resolve\|Dismiss/{id}` | Status transitions (Resolve/Dismiss store notes) |
| `/Notifications` | Own inbox (`?unreadOnly=true`, `?page=n`) |
| `POST /Notifications/Read/{id}` · `POST /Notifications/ReadAll` | Mark read, scoped to the recipient |

### Architecture

`AlertsController` → `IAlertService` → `AlertService` → EF Core → DTOs →
`AlertsIndexViewModel`/`AlertDetailsViewModel` → Razor, and
`NotificationsController` → `INotificationService` → `NotificationService` →
`Notification` + `NotificationPreference` rows. Controllers stay thin (parameter binding,
authorization, TempData messaging) and no EF Core query appears in a view. The existing
`Alert`, `Notification`, `AlertSeverity` and `AlertStatus` types are reused — Phase 10 adds
no duplicate alert entity and no second database.

### Alert categories

Categories are a controlled enum (`AlertCategory` in `Application/Alerts`), stored in the
existing `Alert.AlertType` int column so no widening migration was needed:
`CrimeSpike = 1`, `UnusualPattern = 2`, `HighRiskArea = 3`, `RelatedCases = 4`,
`SecuritySystem = 5`. Labels come from `AlertCategories.Label(...)`; arbitrary category
strings are not used anywhere.

### Generation

Generation is explicit, deterministic and never runs while browsing `/Alerts`, loading the
dashboard, or rendering risk results. The only trigger is `POST /Alerts/Generate` from the
Alerts page header, which requires the `AlertsManage` policy and a valid anti-forgery
token. No background-job framework is introduced. Each pass reads one analysis window
(clamped to 7–365 days, 25 alerts per run maximum) and derives four candidate families:

- **High-Risk Area** — risk indicators from `IRiskAnalysisService` at level ≥ 4 with
  sufficient data; the indicator's stored explanation becomes the alert description
  verbatim, so there is no second risk formula inside `AlertService`.
- **Crime Spike / Unusual Pattern** — anomaly candidates from the same analysis run
  (volume spikes map to Crime Spike, everything else to Unusual Pattern).
- **Related Cases** — persons recorded as a suspect on two or more cases inside the
  window, from existing `CasePerson` rows.
- **Security / System** — failed sign-in counts already present in the audit trail.

### Deduplication

Every generated alert carries a stable `AlertKey`
(`{category}:{sourceType}:{sourceId}:{windowStart}-{windowEnd}`, plus the anomaly type for
anomaly rows) alongside its `WindowStart`/`WindowEnd`. `IX_Alerts_AlertKey` is a **unique
filtered index** (`[AlertKey] IS NOT NULL`), so the database rejects duplicates while
manually raised alerts (key `NULL`) stay unaffected. The service additionally deduplicates
within and against persisted keys, so a second pass over the same window reports
`0 created / n skipped`. Migration
`20261002114919_AddAlertDeduplicationKey` adds `AlertKey`, `WindowStart`, `WindowEnd` and
that index; it has been inspected (no shadow properties, no new foreign keys) and is not
auto-applied.

### Filtering

`/Alerts` filters are plain GET query strings handled server-side: `severity`, `status`,
`category`, `dateFrom`, `dateTo`, `caseId`, `page` — e.g. `/Alerts?severity=High&status=New`.
Submitted values are echoed back into the filter controls, pagination links preserve the
active filter set, unparsable values fall back to "all" instead of failing, and queries use
projections, `AsNoTracking()`, database-side counts and paging.

### Notifications, preferences and ownership

Generating an alert fans out in-application notifications (`DeliveryChannel = 1`; no SMTP
exists in this phase) to the roles that receive that category: Crime Intelligence →
Administrator/Senior Officer/Analyst, Investigation → Administrator/Investigator/Senior
Officer, System → Administrator. Stored `NotificationPreference` rows that disable a
category are honoured — disabled recipients are filtered out, never bypassed. Fan-out is
idempotent per recipient via the alert key stored as `Notification.CorrelationId`.

Ownership is enforced inside the database query (`RecipientId == actorUserId`), not by
hidden form fields; `/Notifications` deliberately exposes no `userId` parameter at all. A
foreign notification id resolves to the same "not found" result as a genuinely missing one,
and the attempt is recorded as a security event. The unread badge in the shell top bar uses
one scoped count query per request.

### Authorization and audit

`NetraPolicies.AlertsRead` (all four operational roles) covers reading; `AlertsManage`
(System Administrator, Senior Officer, Police / Intelligence Analyst) covers generation and
status transitions, which makes Investigator read-only. Sidebar visibility is presentation
only — the policies are enforced on every action. The central `AuditService` records
`AlertGenerated`, `AlertViewed`, `AlertAcknowledged`, `AlertResolved`, `AlertDismissed`,
`NotificationCreated`, `NotificationMarkedRead` and `NotificationAccessDenied`.

### UI, no JavaScript and database caveat

Both pages reuse the Phase 3–9 design system (`_PageHeader`, `_Breadcrumbs`, `_StatCard`,
`_StatusBadge`, `_EmptyState`, `_Pagination`, `netra-*` CSS) and match netra.pdf page 12:
header with real actions, four real-count summary cards, a filter band, and a card feed with
severity/status/category badges, timestamp, analytical basis and triage actions. Everything
is server-rendered Razor — no `<script>`, inline handlers or `javascript:` URLs. Phase 10
tests (alert and notification services plus `/Alerts` and `/Notifications` integration)
run against EF Core InMemory through the application test host; they are not live SQL
Server tests, and the Phase 10 migration has not been applied to a real database.

## Reports & Intelligence Summaries (Phase 11)

`/Reports` is the report library and document viewer described in netra.pdf page 10. A
report is a server-rendered document assembled from data NETRA already holds: cases,
analytics, risk, dashboard and alert figures. Nothing in a report is simulated, sampled,
randomised or predicted, and an empty source renders an honest empty state instead of a
fabricated document.

The functional reference for this phase is the NETRA report specification supplied as a
`.docx` attachment; that file is not present on disk in this workspace, so the phase was
built against the written Phase 11 requirements and netra.pdf page 10, and this limitation
is recorded here rather than guessed around.

### Routes

| Route | Method | Policy | Purpose |
|---|---|---|---|
| `/Reports` | GET | `netra.reports.read` | Paged, filterable report library |
| `/Reports/Details/{id}` | GET | `netra.reports.read` | The report document itself |
| `/Reports/Generate` | GET, POST | `netra.reports.manage` | Generation form and explicit generation pass |
| `/Reports/Create` | GET, POST | `netra.reports.manage` | Analyst-authored report |
| `/Reports/Edit/{id}` | GET, POST | `netra.reports.manage` | Edit an authored report only |
| `/Reports/Archive/{id}` | POST | `netra.reports.manage` | Move a report out of the working set |
| `/Reports/Export/{id}` | GET | `netra.reports.read` | Server-generated CSV of the same document |

Generation, archiving and editing are POST-only with antiforgery validation. Reading a
list page never generates or refreshes a report.

### Architecture

`ReportsController` → `IReportService` → `ReportService` → the existing
`ICaseService`, `IAnalyticsService`, `IRiskAnalysisService`, `IDashboardService` and
`IAlertService` → DTO → view model → Razor. The controller only maps requests and
operation results; `ReportService` only orchestrates. No SQL lives in a view, and no
analytics or risk formula is re-implemented inside the report layer — every figure in a
report is read from the same service the corresponding page uses, so the two cannot
disagree.

### Report types

Five generated documents plus authored content, all on the existing `ReportType` enum
(no duplicate type was introduced):

- **Case Summary** — classification, persons of interest, locations, evidence summary and
  timeline for one case.
- **Investigation Summary** — the same case facts plus the full timeline and
  non-private investigation notes.
- **Crime Trend Analysis** — volume, crime-type, district and severity breakdowns from
  `IAnalyticsService`.
- **Risk Assessment** — district indicators from `IRiskAnalysisService`, labelled
  analytical indicators, never predictions.
- **Executive Intelligence Summary** — dashboard, analytics, risk and alert figures with
  decision-support wording only.
- **Analyst-authored briefing** (`Custom` / `IntelligenceBrief`) — written by a person.

`ReportTypes.Analytical` (`RiskAssessment`, `StatisticalAnalysis`, `IntelligenceBrief`) is
the content gate used by both the list and the document, so analytical content is filtered
in the query rather than hidden with CSS.

### Storage: parameters, not duplicated data

The existing `Report` entity stores the document shell (title, summary, status, source,
attribution, reporting period) plus the **generation parameters** as JSON in
`Report.Parameters`. Report sections are recomputed from live records each time the
document is opened, so a report cannot drift out of date and no case, analytics or risk row
is copied into the report tables. Phase 11 added only the authoring columns
(`Source`, `Content`, `PeriodFrom`, `PeriodTo`, `ArchivedAt`) in migration
`20261002123022_AddReportAuthoringFields`.

### System-generated versus authored

`Report.Source` separates immutable system facts from human text. An analyst-authored
report is edited through `/Reports/Edit/{id}`; attempting to edit a system-generated
report redirects with an explanation, and archived reports are read-only. Analyst
observations supplied at generation time are stored separately and render in their own
clearly labelled "Analyst-authored observations" section, so authored prose can never be
mistaken for a system fact.

### Search, filters and pagination

The library filters server-side on a GET query string: free-text search (title, summary,
author), report type, prepared-by source, status, group tab, and page number. Search and
paging run in the database query, not in memory, and every submitted value is re-rendered
into the filter band so the page round-trips. Invalid enum values, negative pages and
garbled dates fall back to safe defaults rather than faulting. Default page size is 12,
capped at 50.

### Print and export

Two server-side paths, no client-side library:

- **Print** — `@media print` in `Web/wwwroot/css/netra.css` switches to a light theme,
  hides the sidebar, topbar, breadcrumbs, filter band, tabs, pagination and every action
  button, and keeps the branded document header, headings, tables and SVG charts intact.
  Verified by rendering `/Reports/Details/{id}` and `/Reports` to PDF in headless Chrome;
  both produce a clean A4/Letter document.
- **Export** — `GET /Reports/Export/{id}` returns a UTF-8 CSV built on the server from the
  same document. Cell values that begin with `=`, `+`, `-` or `@` are prefixed so a
  spreadsheet cannot execute authored text.

Charts reuse the Phase 5 `IChartRenderer` registry (line, donut, bar). They are
deterministic server-rendered SVG with text labels, so they print and survive HTML
encoding without any charting script.

### Authorization and data minimization

Three dedicated policies are registered in `Web/Program.cs`: `netra.reports.read` (all four
operational roles), `netra.reports.analysis` and `netra.reports.manage` (System
Administrator, Senior Officer, Police / Intelligence Analyst). Investigators may read cases
and non-analytical reports but are refused analytical content, generation, authoring and
archiving. A report the reader may not open returns the same `404` as a missing one, so
existence is never confirmed, and the refusal is audited as `ReportAccessDenied`.

Reports carry case numbers, crime types, districts, severities, counts and analytical
indicators only. They never include passwords or security settings, national identity
numbers, phone numbers, e-mail addresses, private home addresses, dates of birth,
raw evidence file paths or checksums, private investigation notes, or audit internals.
`Details_NeverCarrySensitiveIdentityOrContactData` sweeps every report page for those
values.

### Audit

All report activity goes through the existing `IAuditService`: `ReportCreated`,
`ReportGenerated`, `ReportViewed`, `ReportEdited`, `ReportArchived`, `ReportExported` and
`ReportAccessDenied`. No parallel audit mechanism was added. A refused read is recorded as
an access denial and never as a view.

### UI, no JavaScript and database caveat

`/Reports` matches netra.pdf page 10: page header with real actions, group tabs, filter
band, real-count summary cards and a card grid with type, period, source, status, section
count and per-card actions. `/Reports/Details/{id}` renders a branded document with
metadata, metrics, section tables, SVG charts, findings, linked cases and a
decision-support footer. Everything is server-rendered Razor using the Phase 3–10 design
system — no `<script>`, inline handler, `javascript:` URL, charting or PDF library. Phase 11
tests (46 service-layer plus 33 HTTP integration) run against EF Core InMemory through the
real application test host; they are not live SQL Server tests, and the Phase 11 migration
has been validated only by generating its T-SQL script, never applied to a real database.

## Bulk Data Import (Phase 12)

Bulk Data Import registers crime cases from a CSV file in controlled batches. The module
follows UPLOAD → VALIDATE → PREVIEW → IMPORT → RESULT, and every stage is a normal
server-side HTTP request handled by `BulkImportController` and `BulkImportService`. No
record enters the case register before the operator confirms the import.

### Routes

| Route | Purpose |
| --- | --- |
| `GET /BulkImport` | Landing page: the workflow, the CSV contract, the limits, and this operator's ten most recent imports. |
| `GET /BulkImport/Upload` | The upload form with the full column contract. |
| `POST /BulkImport/Upload` | Stores the file and registers a pending job. Nothing is parsed and no row is read. |
| `GET /BulkImport/Validate/{id}` | Parses and checks every row, staging the outcome for review. |
| `GET /BulkImport/Preview/{id}` | Read-only preview of the accepted rows. Requires a validated job. |
| `POST /BulkImport/Import/{id}` | Runs the import inside one transaction. |
| `POST /BulkImport/Cancel/{id}` | Ends the job without creating anything. |
| `GET /BulkImport/Result/{id}` | Totals, the cases created, the problems recorded, and navigation onward. |

`/BulkImport/Import/{id}` and `/BulkImport/Cancel/{id}` accept POST only, so a `GET`
returns 405; both carry an anti-forgery token, and a second submission of the same job is
refused because the job is no longer in a state that permits an import.

### CSV contract

`BulkImportSchema` ("crime-case-v1") defines twelve columns, all of which map onto fields
the existing `CrimeCase` domain already stores — no second case model and no invented
column. Required: `CaseNumber`, `Title`, `CrimeType`, `IncidentDate`, `Severity`,
`Status`. Optional: `Description`, `District`, `PoliceStation`, `Address`, `Latitude`,
`Longitude`. Headers are matched case-insensitively and in any order; an unknown column
makes the file unusable rather than being silently dropped. Reference columns accept the
configured code or its full name.

### Validation

`CsvReader` is a bounded, RFC-4180-style parser written in C#: quoted fields, doubled
quotes, embedded delimiters and line breaks, with a hard ceiling on records, characters
per row and total bytes, so an oversized or pathological file is refused instead of
buffered. `BulkImportService.ValidateAsync` then checks each row and returns a strongly
typed `BulkImportValidationDto`. Every issue names the row number, the column, a stable
code from `BulkImportIssueCodes` (`REQUIRED_MISSING`, `DATE_IN_FUTURE`,
`CASE_NUMBER_EXISTS`, `STATION_DISTRICT_MISMATCH`, `COORDINATES_INCOMPLETE`, …) and a
message. A supplied value is echoed only for columns flagged `SafeToEcho`; free-text
columns are described rather than repeated, so case narratives are never re-rendered.
Issues beyond 500 per job are counted, not stored, and pages show at most 100.

### Duplicates

The existing case-number uniqueness rule is the only rule. A case number already in the
register, or repeated inside the same file, is **skipped** — never updated and never
overwritten — and reported as a duplicate on the validation, preview and result pages.
Everything else in that row is left untouched.

### Import transaction

`ImportAsync` re-reads the stored file, re-checks each row against the current database
(the register may have moved since validation), and writes the accepted cases in a single
`IDbContextTransaction`: Begin → re-check → import → `SaveChangesAsync` → Commit, with
Rollback on any failure. Each accepted row becomes a `CrimeCase` in exactly the shape the
case register already uses — plus its `Location`/`CaseLocation` pair when the row carries
a district, and an opening `Investigation` — so imported cases need no special handling
in Cases, Dashboard, Analytics, Risk, Alerts or Reports; those modules read the same rows.
If the file no longer matches its validated outcome, or a case number appeared meanwhile,
the run is refused rather than partially applied. The result page states exactly what was
created, skipped and rejected.

### Limits

2 MB per file, 5,000 data rows, 50 rows on the preview page, 500 stored issues (100
shown), 10 recent jobs, 25 reference values named per group. The landing and upload pages
print the configured numbers rather than hard-coded prose.

### Authorization and audit

`NetraPolicies.BulkImportManage` (`netra.bulkimport.manage`) guards the whole controller
server-side and allows System Administrator, Senior Officer and Police / Intelligence
Analyst. The sidebar entry in `NetraNavigation` lists exactly that role set, so the link is
a convenience and never the control — a request to any route is authorised on the server.
A job is visible only to the operator who uploaded it — another user's job id
redirects to the landing page and writes an access-denied audit entry. `IAuditService`
records upload, validation, preview, import started, import completed and access denied,
plus the per-case creation entries; file contents are never stored in the audit trail.

### Temporary files

Uploads go through the existing `IFileStorageService`, which sanitises the name, generates
the on-disk name, confines writes to the configured storage root and rejects a resolved
path outside it. The client-supplied file name is kept as a display label only, so a
path-traversal-style name is refused before storage, and stored content is only ever
parsed as text — never executed, rendered inline or served back.

### UI, no JavaScript and database caveat

The five pages use the Phase 3–11 design system: shared layout, breadcrumbs, page header,
cards, `_StatusBadge`, `_EmptyState`, `_Alert` and button styles. Preview pages state
plainly that a preview writes nothing, and the result page links straight into the case
register. Phase 12 was verified visually from the real rendered HTML at 1440px and
1000px; no page overflows horizontally, and the wide preview table scrolls inside its own
card. There is no JavaScript anywhere in the module — no script tag, inline handler,
`javascript:` URL, `fetch`/AJAX call or client-side CSV parsing. Phase 12 tests (62
service/reader, 31 HTTP integration and 4 import-enum tests) run against EF Core InMemory
through the real application test host; they are not live SQL Server tests, and no Phase 12
migration exists because the schema already covers the workflow.

## User Management + Audit Logs (Phase 13)

User Management administers the ASP.NET Identity accounts NETRA already signs people in
with, and the Audit Log reader presents the append-only trail those services already write.
Neither module introduces a second user system, a second audit system, a new table or a new
column: `ApplicationUser`, the Identity role tables and `AuditLog` are the only stores, and
no migration was required.

### Routes

| Route | Purpose |
| --- | --- |
| `GET /Users` | Accounts with server-side search, role and status filters and pagination. |
| `GET` / `POST /Users/Create` | Register an account, grant roles, hand the password to Identity. |
| `GET` / `POST /Users/Edit/{id}` | Profile and role assignment, with before/after audit. |
| `GET /Users/Details/{id}` | One account: profile, roles, account state, activation control. |
| `POST /Users/Activate/{id}` · `POST /Users/Deactivate/{id}` | Account state, POST only. |
| `GET /AuditLogs` | The trail, newest first, with server-side filters and pagination. |
| `GET /AuditLogs/Details/{id}` | One entry with its recorded values and request context. |

`/Users/Activate/{id}` and `/Users/Deactivate/{id}` accept POST only, so a `GET` returns
405/404 and no link can change an account; both carry an anti-forgery token. Every mutating
`POST` pins its target with `[FromRoute]`, because MVC binds form values ahead of route
values — a posted `Id` field can therefore neither retarget an edit nor aim a deactivation
at another account.

### Services

`IUserManagementService` / `UserManagementService` owns the account rules and talks to
Identity through `UserManager<ApplicationUser>` and `RoleManager<IdentityRole<Guid>>` —
role membership is never written with raw SQL, and no `ApplicationUser` is ever bound
directly from a form. `IAuditLogQueryService` / `AuditLogQueryService` is read-only: it
selects, filters and pages the existing trail and inserts nothing. Controllers stay thin —
authorisation, view-model assembly and redirects, with no EF query or account rule in them.

### Search, filters and pagination

`GET /Users` filters on the server: `search` covers the username, email, first and last
name, badge number and phone as stored by Identity, `role` resolves through the Identity
user-role table, and `status` maps onto the state Identity already keeps — `Active`,
`Inactive` (`IsActive == false`) and `Locked` (`LockoutEnd` in the future). `GET /AuditLogs`
filters by actor, record type, action and a date range whose final day is inclusive. Both
queries use `AsNoTracking`, project to DTOs, and page with `Skip`/`Take`: 20 accounts per
page (100 maximum), 25 audit entries per page (100 maximum). Nothing loads a whole table,
and the filter pick lists are built from values that actually occur in the trail, so the UI
cannot offer a filter for a column the model does not have.

### Roles, account state and self-protection

The four seeded NETRA roles are the only assignable ones; a role that does not exist in the
role store is rejected rather than created. Creating, editing, role changes, activation and
deactivation all go through `UserManager`, so password hashes, security stamps, concurrency
stamps and lockout counters stay under Identity's control. Three destructive administrative
changes are refused server-side and reported to the operator instead of being performed
silently: removing the administrator role from your own account, deactivating your own
account while signed in, and stripping the administrator role from the last remaining
administrator account. Each refusal also writes a `User Access Denied` audit entry.

### Audit trail

`IAuditService` and its existing constants carry every write; Phase 13 added only the
actions genuinely missing: `UserCreated`, `UserEdited`, `UserRoleChanged`, `UserActivated`,
`UserDeactivated` and `UserAccessDenied`. Role entries record the before and after role
lists; profile entries record field-by-field `Name: "old"` / `Name: "new"` pairs; a save
that changes nothing writes nothing. Passwords, hashes and tokens never reach a payload —
the create flow records roles only.

### Authorization and data minimization

`NetraPolicies.UserManagementManage` (`netra.user.management`) guards the whole
`UsersController` and is held only by System Administrator. `NetraPolicies.AuditRead`
(`netra.audit.read`) guards `AuditLogsController` and allows System Administrator, Senior
Officer and Police / Intelligence Analyst. Both are enforced server-side on every action;
the sidebar entries in `NetraNavigation` are a convenience, not the control. No route writes
to the trail from a browser-supplied value, and the audit module exposes no write verb at
all. Account and entry pages print profile, role, state and recorded values — never a
password hash, security stamp, concurrency stamp, token or the raw credential a form
carried. Audit payloads render as escaped text, so an entry containing markup displays as
that markup's source rather than executing it.

### UI, no JavaScript and database caveat

The six pages reuse `_PageHeader`, `_FilterPanel`, `_DataTable`, `_StatusBadge`,
`_EmptyState`, `_Pagination`, `_FormSection` and `_Breadcrumbs` on the Phase 3–12 design
system — no new framework, no new component vocabulary. Pagination links carry the active
filters, so paging never loses a search. Phase 13 was verified visually from the real
rendered HTML at 1440px and 1000px: no page overflows horizontally, and the two data-dense
tables scroll inside their own `_DataTable` wrapper at 1000px. There is no JavaScript
anywhere in the module — no script tag, inline handler, `javascript:` URL or `fetch`/AJAX
call; filtering, paging and account state are all ordinary server-rendered requests. Phase
13 tests (68 service-layer and 89 HTTP integration) run against EF Core InMemory through the
real application test host; they are not live SQL Server tests, and no Phase 13 migration
exists because the Identity and `AuditLog` schemas already cover the work.

## Settings + Support (Phase 14)

Settings is self-service: every page edits the account the request is signed in as, and
there is no user picker anywhere in the module. Support is documentation only — it describes
the installation as it was actually built, and it offers nothing this deployment cannot
perform. Neither page introduces a store: `UserSettings`, `NotificationPreferences`,
`SecuritySettings` and `SystemPreferences` were all created by the initial migration, so
Phase 14 needed no new table, no new column and no migration.

### Routes

| Route | Purpose |
| --- | --- |
| `GET /Settings` | Hub: the areas this account can open, each summarised from its own data. |
| `GET` / `POST /Settings/Account` | Name, badge number and rank as other officers see them. |
| `GET` / `POST /Settings/Security` | Change your own password; account state read from Identity. |
| `GET` / `POST /Settings/Notifications` | Which alert categories may create your inbox items. |
| `GET` / `POST /Settings/Appearance` | Row spacing and motion, applied by the server. |
| `GET /settings/system-preferences` | Administrator-only, read-only view of the stored setting tables. |
| `GET /Support` | Module guidance, workflows, the access model and common questions. |

The four editable areas accept POST only for writes — no `GET` changes anything — and each
form carries `[ValidateAntiForgeryToken]`. Nothing accepts a user id from the browser: the
service is handed the id read from the authenticated principal, so a hand-crafted form
cannot aim a change at another account. `/support` and `/settings/system-preferences` are
GET-only routes, so a POST to either is rejected before any handler runs.

### Services

`ISettingsService` / `SettingsService` (`Application/Settings`, `Infrastructure/Services`)
owns the rules and is the only place that touches `UserManager<ApplicationUser>`,
`NotificationPreference` and `UserSetting`. Controllers stay thin: authorisation, view-model
assembly, `ModelState` carry-over and redirects. Profile edits reuse the existing `Edited`
audit action and the field-by-field diff convention from User Management; no second
preferences table and no second audit writer.

### What each page exposes

**Account** edits first name, last name, badge number and rank — trimmed, length-capped
(100/100/50/100), with the badge checked against every other account so it stays unique when
supplied. Username, email, roles and activation state render read-only with a pointer to User
Management, because they belong to the sign-in identity and are an administrator's decision.
**Security** takes current, new and confirm password and validates against the policy read
from `IdentityOptions`, so the page cannot promise rules the server does not apply; on
success Identity's security stamp moves, the current session is refreshed, and the message
states that other sessions will be signed out. The same card reports email confirmation,
two-factor enrolment, lockout state and last sign-in — and says plainly when this deployment
records no sign-in time rather than implying the account never signed in.
**Notifications** writes the existing `NotificationPreference` rows for the categories the
alert generator can actually produce; a category switched off stops new notifications of
that kind and leaves existing items and alerts untouched, and leaving everything unchecked is
a valid choice. **Appearance** stores two rows in `UserSettings` (`ui.density`,
`ui.reduced-motion`).

### Appearance without JavaScript

The two appearance choices select between fixed overrides already in `netra.css`: the server
puts `netra-density--comfortable` / `netra-density--compact` and, when motion is reduced,
`netra-motion--reduced` on the `<body>` while rendering. There is no theme framework, no
uploaded or generated stylesheet, no `localStorage` and no inline script — the setting is
read from the database on every request, which is also why it follows the officer to another
machine.

### System preferences are read-only

`/settings/system-preferences` lists what NETRA has actually recorded in its own setting
tables and nothing more. Today the application writes no rows there, so the page says so
instead of inventing an editor: the tables exist and are mapped, and no safe key exists to
offer. The values that do configure the running application — connection string, AI provider
endpoint, file storage root, signing keys, environment variables — live in deployment
configuration and are deliberately not read or displayed, because a form that could rewrite
runtime configuration is a remote-code-execution path wearing a login screen. The page links
to the screens that genuinely administer accounts, alert generation, import limits and the
audit trail.

### Support has no request form

There is no ticket entity, no configured recipient address and no message delivery of any
kind in this deployment, so a form would only discard what was typed into it. `/Support`
therefore documents what exists: the sidebar groups, per-module guidance with the route and
the roles that can open it, the register/import/report/analytics workflows, how authorisation
is decided, what the audit trail does and does not record, and nine common questions built
from HTML `details` elements — no script-based accordion. It states in the page that there is
no external support channel and no support-request form, and it shows the caller's own
session facts (username, name, badge, rank, roles) read server-side, never posted.

### Authorization, audit and data minimisation

Both controllers require authentication; the system-preferences page additionally requires
`NetraPolicies.Administration` (System Administrator). The Settings hub and the sidebar omit
that card for other roles, but the policy on the action is the control. Writes go through
`IAuditService` with the existing `Account` entity type and the owner's id as both actor and
subject: `Edited` for profile fields, plus `PasswordChanged`,
`NotificationPreferencesUpdated` and `AppearancePreferencesUpdated`. A password change
records only the fact that it changed — no password, hash, security stamp, concurrency stamp
or token reaches an audit payload, `TempData`, `ViewData` or the log, and a rejected change
re-renders with its validation messages while the submitted password values are dropped from
the model, so nothing secret is echoed back in the response body.

### UI, no JavaScript and database caveat

Seven pages reuse `_PageHeader`, `_Breadcrumbs`, `_StatusBadge` and `_EmptyState` inside the
shared `_Layout` shell, on the Phase 3–13 design system; the only stylesheet in the
repository is still `Web/wwwroot/css/netra.css`. Phase 14 was verified visually from the real
rendered HTML at 1440px and 1000px, in both densities and with invalid submissions displayed:
validation messages render beside the field they belong to, and long identities wrap inside
their column instead of printing over the value beside it. There is no JavaScript anywhere —
no script tag, inline handler, `javascript:` URL, `fetch`/AJAX call or browser storage — and
no `.js`, `.ts` or `package.json` in the tree. Phase 14 tests (41 service-layer, 47 Settings
HTTP integration and 32 Support HTTP integration) run against EF Core InMemory through the
real application test host; they are not live SQL Server tests, and no Phase 14 migration
exists because the settings and preference schemas were already in place.

## Database setup

Database initialisation is controlled by `Database:AutoMigrate`.

| Environment | Default | Behaviour |
|---|---|---|
| Development | `true` | Migrate and seed on startup |
| Production / Staging | `false` | No automatic migration |

Because SQL Server is not always available during UI work, set it to `false` to start
the application against an existing database without touching it:

```bash
dotnet run --project Web          # honours Web/appsettings.Development.json
# or override per run:
Database__AutoMigrate=false dotnet run --project Web
```

Database exceptions are **never swallowed**. When initialisation is enabled and the
server is unreachable, startup fails with the provider error rather than starting in a
broken state.

Outside Development, apply migrations explicitly before starting:

```bash
dotnet ef database update \
  --project Infrastructure/NETRA.Infrastructure.csproj \
  --startup-project Web/NETRA.Web.csproj
```

To create a new migration after changing the model:

```bash
dotnet ef migrations add <MigrationName> \
  --project Infrastructure/NETRA.Infrastructure.csproj \
  --startup-project Web/NETRA.Web.csproj \
  --output-dir Data/Migrations
```

### Verifying the migration chain

Before touching any server, the chain can be inspected and rendered to SQL offline:

```bash
dotnet ef migrations list --project Infrastructure --startup-project Web
dotnet ef migrations has-pending-model-changes --project Infrastructure --startup-project Web
dotnet ef migrations script --idempotent --project Infrastructure --startup-project Web -o schema.sql
```

`list` must show every migration in `Infrastructure/Data/Migrations` in timestamp order,
`has-pending-model-changes` must report no changes, and `schema.sql` must be reviewed line
by line before it is run anywhere. A migration file that has no matching `.Designer.cs` is
invisible to EF: `Database.MigrateAsync()` skips it silently and the model snapshot already
counts its change as applied, so the schema difference never surfaces in a test.
`SqlServerSchemaAndTranslationTests` fails if any migration type loses its `[Migration]`
attribute, and asserts that the generated schema contains every module's table, the unique
`CrimeCases.CaseNumber` index, the filtered unique `PersonLocations` index and the
`CK_NetworkEntities_ExactlyOneReference` check constraint.

The same tests compile NETRA's projections with the real SQL Server provider without
connecting, which is how the enum-name and trimmed-name server-side translations are pinned.

## Deployment and operations (Phase 15)

### Production publish

```bash
dotnet publish Web/NETRA.Web.csproj -c Release -o publish
```

The published folder is framework-dependent and self-contained in content: the four NETRA
assemblies (Razor views are compiled into `NETRA.Web.dll`), every third-party assembly the
runtime needs, `web.config` for IIS, `appsettings.json`, and `wwwroot` served through the
static-asset manifest. Nothing development-only is required to start it — `dotnet run`,
`launchSettings.json` and `appsettings.Development.json` are all optional at that point.

Run it with the environment configured externally:

```bash
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__DefaultConnection="..." \
./NETRA.Web
```

### HTTPS, headers and cookies

The pipeline emits, on every response:

| Header | Value | Why |
|---|---|---|
| `Content-Security-Policy` | `default-src 'self'; script-src 'none'; connect-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'` | `script-src 'none'` makes the no-JavaScript rule browser-enforced, so stored data cannot execute script even if a server-side check were bypassed. `style-src` keeps `'unsafe-inline'` because server-rendered views carry inline `style` attributes for computed widths and SVG geometry; there are no `<style>` blocks and no external stylesheet but the site CSS. |
| `X-Content-Type-Options` | `nosniff` | no MIME sniffing on stored uploads |
| `X-Frame-Options` / `frame-ancestors` | `DENY` / `'none'` | no click-jacking frame |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | case identifiers do not leak to third parties |
| `Permissions-Policy` | camera/microphone/geolocation/payment all disabled | the app needs none |
| `Strict-Transport-Security` | `max-age=2592000` | added by HSTS outside Development; ASP.NET Core deliberately omits it for `localhost` |

Cookies: the Identity session cookie is `HttpOnly`, `SameSite=Lax` and — outside
Development — `Secure`, so it survives a TLS-terminating proxy without ever being sent over
plain HTTP. The anti-forgery cookie is `HttpOnly`, `SameSite=Strict` and forced `Secure` on
the same condition; Phase 15 measured that the framework leaves `Secure` off under
`SameAsRequest`, so it is set explicitly. `UseHttpsRedirection` upgrades every plain-HTTP
request with a `307`.

### Backup, recovery and retention (operational recommendations)

NETRA ships no backup subsystem, deliberately: it is an operator responsibility outside the
application.

- **Database.** Take a `FULL` backup on a schedule matched to how much investigation work
  can be re-entered, plus `DIFF` backups between them and a `LOG` backup every few minutes if
  the recovery model is `FULL`. Restore testing is the only proof a backup works.
- **Migrations.** Every migration here is expand-only and has a working `Down`; the reviewed
  script contains no `DROP TABLE`, no `TRUNCATE` and no data deletion. Even so, back up
  immediately before applying, and apply with `dotnet ef migrations script` reviewed first
  rather than by letting the application do it.
- **Import recovery.** An import job writes its accepted rows and its job status in one
  transaction, so a failed import leaves no partial batch. Re-running a job skips rows whose
  case numbers already exist, which makes replay safe but not automatic.
- **Audit retention.** `AuditLogs` is append-only from the application's point of view and is
  never purged by code. Retention is therefore an operator decision; archive to a separate
  store before any deletion, because the trail cannot be reconstructed afterwards.
- **Uploaded files.** Evidence and imported images live under `App_Data/evidence` with
  generated names and are covered by no retention job. Include that directory in the file
  backup, and note that the database stores paths — restoring one without the other leaves
  records whose bytes are gone.

### Known limitations

- **Never yet run against a live SQL Server instance.** Everything provable offline is proven
  (schema script, query translation, constraints, security headers, published artifact). The
  items that need a real server remain unverified: applied-migration execution, collation
  behaviour on case-insensitive searches, real transaction rollback, constraint violation
  errors under load, and page response times. See [Testing status](#build-and-test).
- **`NetworkEntities.CentralityScore` is `float(10)`.** `HasPrecision(10, 4)` on a `double`
  maps to SQL Server `float(10)`, which is 4-byte `real` (~7 significant digits), not
  `decimal(10,4)`. Centrality is a 0–1 ranking score, so the loss is immaterial today; a
  future change should either drop the precision annotation or make the column `decimal`.
- **`AuditLogs → ApplicationUsers` cascades on delete.** NETRA has no user-deletion feature
  (accounts are deactivated), so nothing in the application can trigger it; a DBA deleting a
  login row directly would still remove that actor's audit history. Change the relationship
  to `Restrict` before any deployment that allows hard user deletion.
- **Timeline event times are wall-clock as entered.** `datetime-local` input is stored
  exactly as typed and displayed the same way, while case, audit and report timestamps are
  UTC. The two are not interchangeable.
- **Bulk import is single-instance.** `ImportJobs` staging rows are owned by the job, and the
  CSV is parsed in one request; concurrent large imports are not throttled.
- **No transient-failure retry.** `UseSqlServer` is configured without
  `EnableRetryOnFailure`, so a momentary cloud-SQL failover surfaces as a failed request.
  Enable it in the deployment profile if the target platform has brief connection blips.
- **Filter pick-lists scan the audit trail.** `AuditLogs` distinct-value queries are indexed
  scans over the whole trail; on a very large trail the audit filter panel is the slowest page.

## Development seeding

`IdentitySeedService` performs seeding in two tiers.

**Roles — all environments.** These four foundational roles are created idempotently:

- System Administrator
- Police / Intelligence Analyst
- Investigator
- Senior Officer

**Demo data and dev admin — Development environment only.** In non-development environments these are skipped entirely and logged.

Demo reference data (5 districts, 3 police stations, 8 crime types) is seeded idempotently.

The development admin account is `admin@netra.local`. Its password is **not** stored in source code. The account is only created when a password is supplied through one of these, in priority order:

1. Environment variable:
   ```bash
   export NETRA_DEV_ADMIN_PASSWORD='Choose-Your-Own-Strong-Password1!'
   ```
2. Configuration key `DevelopmentSeed:AdminPassword` in `Web/appsettings.Development.json` (ships empty).

If neither is set, **no admin user is created** and a warning is logged. There is no built-in fallback password. The password must satisfy the configured Identity policy: minimum 8 characters with upper case, lower case, digit, and non-alphanumeric character.

> The development admin password is a local convenience only. It is not a production credential. Production environments must provision real accounts, and this seed path never runs there.

## Build and test

```bash
dotnet restore
dotnet build
dotnet test
```

UI tests boot the real application with `WebApplicationFactory`, substituting an
in-memory provider for SQL Server, so authentication and shell rendering are verified
without a database server. The suite (996 tests) covers shell rendering, authentication
and authorization, the case module end to end (list filtering and pagination, case
creation and editing, person/location associations, evidence, notes, timeline events,
investigator assignment and the investigation workspace), the Phase 5 dashboard
and analytics modules (service aggregation, filters, SVG chart renderers, page
rendering, authorization and the no-JavaScript guarantee), the Phase 6 risk and
map modules (deterministic scoring, anomaly detection and explanations, coordinate
validation, server-side SVG rendering, quick view, honest empty states, authorization
and the no-JavaScript guarantee), Phase 7 network analysis (bounded relational
traversal, explicit-link cycles, filters, metrics, SVG rendering, authorization,
empty states, and no-JavaScript output), and Phase 8 image-assisted investigation
(server-side upload validation including magic bytes, secure storage naming,
deterministic mock matching, investigative-lead wording, authorization, audit
logging, resource limits and the no-JavaScript guarantee), and Phase 9 AI assistant
(intent resolution, approved-query execution, data minimization, prompt-injection
refusal, session ownership, provider fallback, result caps, audit logging,
authorization and the no-JavaScript guarantee), and Phase 10 alerts and notifications
(deterministic generation from existing risk, anomaly, association and audit findings,
AlertKey deduplication across repeated runs, server-side filters and value persistence,
triage lifecycle, notification fan-out with preference respect, recipient ownership,
authorization, audit logging, honest empty and not-found states, and the no-JavaScript
guarantee), and Phase 11 reports and intelligence summaries (generation from recorded case,
analytics, risk, dashboard and alert data with no duplicated algorithms, parameter-not-result
storage, system-generated immutability, analyst authoring and edit rules, server-side search,
filters and pagination, the analytical content gate and safe not-found behaviour, data
minimization sweeps, CSV export including formula-injection neutralisation, audit events,
honest empty states, and the no-JavaScript guarantee), and Phase 12 bulk data import (bounded
CSV parsing, row validation and issue codes, duplicate skip-only behaviour, one-transaction
import, job ownership, storage limits, audit events, and the no-JavaScript guarantee), and
Phase 13 user management and audit logs (Identity-backed listing, search, role and status
filters with server-side pagination, create/edit through `UserManager` with no mass
assignment of identity state, role changes audited with before and after values, POST-only
activation and deactivation with anti-forgery and route-pinned targets, the three
administrator self-protection refusals, read-only projected audit queries with filters built
from real columns only, escaped audit payloads, data-minimization sweeps that prove no
credential material reaches a page or a trail entry, authorization per policy on every route,
anonymous redirect and unauthorized 403 for each route, and the no-JavaScript guarantee), and
Phase 14 settings and support (self-scoped profile editing with badge-number uniqueness,
password change validated against the policy Identity actually applies with no secret echoed
in a response or recorded in a trail entry, notification preferences written to the existing
rows and no second preferences table, appearance stored as two setting keys and applied by a
server-rendered body class, read-only system preferences that expose no runtime-configuration
editor, a support page documenting only what this deployment really offers, anonymous redirect,
unauthorized 403, 405 for POST to GET-only routes, anti-forgery on every state-changing POST,
typed view models with no binding of identity state, and the no-JavaScript guarantee), and
Phase 15 integration (a SQL Server provider suite that is separate from the InMemory suites
because it needs no server: every migration type must still be discoverable by EF, the chain
must stay ordered and duplicate-free, the generated schema must contain all 42 mapped tables,
the unique case-number index, the filtered unique person-location index and the network
reference check constraint, and the enum-name and trimmed-name projections must still compile
to SQL), and the navigation integrity check that every call-to-action on an implemented page
resolves to a real module rather than the placeholder controller.

Package vulnerability audit:

```bash
dotnet list package --vulnerable --include-transitive
```

Expected result: no vulnerable packages in any of the five projects.

## Technology constraints

Application code is C# with server-rendered Razor and CSS. Phase 4 verification found
no JavaScript application files, no TypeScript application files, no React, Angular or
Vue frontend, no Node.js backend, no Python backend, no `<script>` blocks used for
application functionality, and no inline JavaScript handlers. JSON configuration files
legitimately exist and are allowed; they are not application scripting. The Phase 14
repo-wide re-audit found the same: no `.js`, `.mjs`, `.jsx`, `.ts` or `.tsx` file, no
`package.json` or lock file, one asset in `Web/wwwroot` (`css/netra.css`), and zero `<script>`
tags, inline event handlers, `javascript:` URLs or browser-storage calls in the views. The
Phase 15 re-audit repeated that result across the whole repository, and the rule is now
enforced twice over: the served pages carry `Content-Security-Policy: script-src 'none'`, so a
browser refuses to run script even if one were ever stored in data.
