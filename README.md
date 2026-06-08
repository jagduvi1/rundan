# Rundan

A private, mobile-first web app for a small group of friends to run group activities together:

- **Tipspromenad** — a geo-located quiz walk. Questions are placed on a map; your phone unlocks
  and buzzes when you reach each one.
- **Quiz** — a regular sit-down quiz, question by question.
- **Boule** — round-based score tracking (and a generic "score game" for anything else).
- …with room to add more activity types later.

Everyone sees a **live shared scoreboard** that updates in real time as answers and scores come in.

---

## The stack (fixed, single application)

| Concern | Technology |
|---|---|
| Framework | ASP.NET Core (C# / .NET 10), a **single** app |
| Client / UI | **Blazor WebAssembly** — C# end to end, stays responsive on weak mobile signal |
| Real-time | **SignalR**, in-process inside the same app (no Azure SignalR Service) |
| Data | **SQLite via EF Core**, a single file on the App Service persistent disk (`/home`) |
| Maps | **Leaflet + OpenStreetMap** tiles (free, no API key) |
| Device APIs | Browser **Geolocation** + **Vibration** via small JS interop |
| Hosting | **One** Azure App Service Plan, **single instance** (SQLite is single-writer) |

HTTPS is required (geolocation needs a secure context) and the UI is mobile-first.

There are **no other Azure resources** — the App Service Plan is the only thing that costs money.

---

## Solution layout

```
Rundan.slnx
global.json                      # pins the .NET 10 SDK
src/
  Rundan.Shared/                 # DTOs, enums, and the SignalR hub contract (shared by both ends)
    Enums.cs
    Contracts/                   # request/response DTOs
    Realtime/                    # IScoreboardClient + message/route name constants
  Rundan.Server/                 # the single ASP.NET Core host
    Program.cs                   # DI, EF Core/SQLite, SignalR, security pipeline, hosting
    RundanOptions.cs             # configuration (access code, admin code, db path)
    Data/                        # AppDbContext, entities, EF migrations
    Services/                    # scoreboard, scoring, join codes, real-time notifier, mapping
    Security/                    # access-code middleware, admin filter, participant resolver
    Hubs/                        # ScoreboardHub (in-process SignalR)
    Endpoints/                   # minimal-API endpoints
  Rundan.Client/                 # the Blazor WebAssembly UI (served by the host)
    Pages/                       # Home, Admin, Manage, Activity
    Components/                  # Scoreboard, JoinPanel, QuizPlay, TipspromenadPlay, BouleBoard, …
    Services/                    # API client, SignalR connection, geo/vibration/Leaflet interop, app state
    wwwroot/js/                  # geo.js, vibrate.js, leaflet.js interop
    wwwroot/css/app.css          # mobile-first styles
```

The **Server** project references the **Client** project and hosts it: `UseBlazorFrameworkFiles()`
serves the WASM bundle and `MapFallbackToFile("index.html")` serves the SPA. One deployable unit.

---

## Data model

Two reusable building blocks cover all current and most future activities:

- **Questions + Answers** drive *Quiz* and *Tipspromenad* (Tipspromenad questions just also carry a
  latitude/longitude/radius).
- **Score entries** (round + points) drive *Boule* and the generic *Score game* — a participant's
  total is the sum of their score entries.

```
Activity (Id, Type, Title, Description, Status, JoinCode⭐, ScoringMode, CreatedUtc, StartedUtc, FinishedUtc)
  ├─ Participant (Id, DisplayName, Token⭐, IsAdmin, JoinedUtc)        unique (ActivityId, DisplayName)
  │     ├─ Answer (QuestionId, ParticipantId, SelectedOptionId?, FreeText?, IsCorrect, AwardedPoints)
  │     │                                                              unique (QuestionId, ParticipantId)
  │     └─ ScoreEntry (Round, Points, Note?, RecordedUtc)
  ├─ Question (Order, Text, Kind, Points, ImageUrl?, Lat?, Lng?, RadiusMeters?, AcceptedFreeTextAnswer?)
  │     └─ AnswerOption (Order, Text, IsCorrect)
  └─ ScoreEntry (→ Participant)
```

`⭐` = unique index. Deleting an `Activity` cascades to all of its children.

- `ActivityType`: `Quiz`, `Tipspromenad`, `Boule`, `ScoreGame`
- `ActivityStatus`: `Draft` → `Open` (lobby) → `Live` → `Finished`
- `QuestionKind`: `MultipleChoice`, `TrueFalse`, `FreeText`
- `ScoringMode`: `HigherWins`, `LowerWins` (ranking only)

The schema is created/updated automatically at startup via `db.Database.Migrate()`, and the database
runs in **WAL** mode for better read concurrency while the single writer is busy.

---

## How the live scoreboard works

1. A client opens the page for an activity and connects to the in-process SignalR hub at `/hub/scoreboard`,
   then calls `JoinActivity(activityId)` to subscribe to that activity's group.
2. Writes (submitting an answer, recording a score, changing status) go through the **REST API**.
3. After each successful write the server rebuilds the scoreboard and pushes `ScoreboardUpdated`
   to the activity's SignalR group — every connected phone updates instantly.
4. The connection auto-reconnects and re-joins the group when mobile signal drops.

The hub is strongly typed (`Hub<IScoreboardClient>`); the message names are shared constants so the
server and the WASM client can't drift apart.

---

## Security & privacy model

This is a private app for friends, kept simple and with **zero extra Azure resources**:

- **Access code** (`Rundan:AccessCode`) — a shared site password. When set, every API and SignalR
  call must present it (header `X-Rundan-Access`, or the SignalR `access_token` for the WebSocket
  handshake). The app shows a one-time gate screen and remembers the code on the device.
- **Admin code** (`Rundan:AdminCode`) — required to create and manage activities (the "Host" panel).
- **Participant token** — issued when you join an activity; the device stores it and presents it
  (`X-Rundan-Participant`) so answers and scores are attributed to you and can't be spoofed.

The Blazor static files themselves aren't gated (there's nothing sensitive in them) — **all data**
access goes through the gated API/hub. Codes are compared in constant time.

> Want stronger auth later? App Service "Authentication" (Easy Auth) can be layered on without adding
> a billable resource, but for a friend group the shared codes are usually enough.

---

## Run it locally

Prerequisites: the **.NET 10 SDK**.

```bash
dotnet run --project src/Rundan.Server
```

Then open the HTTPS URL it prints (e.g. `https://localhost:7150`). On first run it creates the SQLite
file under `src/Rundan.Server/App_Data/rundan.db`.

In `Development` the access and admin codes are empty (no gate) for zero-friction testing. The database
is migrated automatically on startup.

> Geolocation only works over HTTPS (or `localhost`). Use the `https://localhost:...` URL, and to test
> the quiz walk on a real phone use a tunnel (e.g. `dev tunnels`) or deploy.

### Configuration

All settings live under the `Rundan` section (use `Rundan__Name` env vars on Azure):

| Setting | Meaning | Default |
|---|---|---|
| `Rundan:AppName` | Name shown in the UI | `Rundan` |
| `Rundan:AccessCode` | Shared site password (empty = open) | empty |
| `Rundan:AdminCode` | Host password for managing activities (empty = open) | empty |
| `Rundan:DatabasePath` | Override the SQLite file path | derived from `HOME` |

---

## Deploy to Azure App Service (the only paid resource)

The app is a single ASP.NET Core site. Any App Service Plan tier with **Always On** works
(B1 is plenty for a friend group). Linux or Windows are both fine.

### 1. Create the plan + web app

```bash
az group create -n rundan-rg -l westeurope

az appservice plan create -n rundan-plan -g rundan-rg --sku B1 --is-linux

az webapp create -g rundan-rg -p rundan-plan -n <your-unique-name> --runtime "DOTNETCORE:10.0"
```

### 2. Required platform settings

```bash
# HTTPS only (also redirects at the platform edge)
az webapp update -g rundan-rg -n <your-unique-name> --https-only true

# WebSockets on (needed for SignalR)
az webapp config set -g rundan-rg -n <your-unique-name> --web-sockets-enabled true --always-on true

# App settings: the access/admin codes (and keep the instance count at 1)
az webapp config appsettings set -g rundan-rg -n <your-unique-name> --settings \
  Rundan__AppName="Vänner & Lekar" \
  Rundan__AccessCode="<pick-a-shared-code>" \
  Rundan__AdminCode="<pick-an-admin-code>"
```

Keep the plan at **one instance** — do **not** enable scale-out. SQLite is single-writer, and the
in-process SignalR hub + the single DB file assume exactly one instance.

### 3. Publish

```bash
dotnet publish src/Rundan.Server -c Release -o ./publish
cd publish && zip -r ../app.zip . && cd ..

az webapp deploy -g rundan-rg -n <your-unique-name> --src-path app.zip --type zip
```

Publishing the **Server** project automatically builds and bundles the Blazor WASM client into
`wwwroot`. On startup the app creates/migrates `D:\home\data\rundan.db` (Windows) or
`/home/data/rundan.db` (Linux) — the persistent `/home` share survives restarts and redeploys.

### Why this stays cheap

- One App Service Plan = the only billable resource.
- No Azure SQL / Cosmos / Storage / SignalR Service — data is a file on the disk you already pay for.
- Map tiles are free OpenStreetMap; no Maps key or quota.

### Backups

The whole database is one file. To back it up, download it from the App Service file share
(Kudu/SSH) — `/home/data/rundan.db` (plus the `-wal`/`-shm` files if present).

---

## Adding a new activity type

The data model is built to extend without schema changes for most games:

1. Add a value to `ActivityType` in `src/Rundan.Shared/Enums.cs`.
2. If it's score-based, you're mostly done — reuse `ScoreEntry`/the boule board, or add a small
   play component under `src/Rundan.Client/Components/` and branch to it in
   `Pages/Activity.razor`'s `Live` switch.
3. If it needs questions, reuse the `Question`/`Answer` machinery (like Quiz/Tipspromenad).
4. Only reach for a new table if the game genuinely needs different data.
