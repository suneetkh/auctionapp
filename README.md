# Live Sports Player Auction Platform

A self-contained ASP.NET Core (.NET 9) application: one process serves the REST API, a SignalR
hub for real-time auction events, and a plain HTML/CSS/JS frontend from `wwwroot`. No npm,
no build step, no Docker, no external database server — SQLite is a single file on disk.

## How to run

Requires the .NET SDK (9.0+).

```
cd App.Api
dotnet run
```

Or open `App.sln` in Visual Studio / Rider and press F5 (set `App.Api` as the startup project).

The app listens on the URL printed in the console (typically `http://localhost:5000` /
`https://localhost:5001` or similar — check the `Now listening on:` line). Open that URL in a
browser; it redirects to the login page.

On first run in the `Development` environment, the app automatically:
1. Applies EF Core migrations to create the SQLite database.
2. Seeds a SuperAdmin user, one demo cricket auction (in Draft status) with rules configured,
   6 demo teams with balances, and 28 demo players with varied roles/base prices.

### Where the database lives

`App.Api/App_Data/app.db` (created automatically). Delete this file (and the `-shm`/`-wal`
companion files, if present) to reset to a fresh seeded state on next run.

### Seeded login

- **Email:** `admin@auction.local`
- **Password:** `Admin@123`
- **Role:** SuperAdmin

Use the Users API (`POST /api/users`, requires SuperAdmin/AuctionAdmin) to create additional
AuctionAdmin or TeamOwner accounts. For a TeamOwner to see their team on the Team Dashboard,
set that team's `ownerUserId` to the new user's id (via `PATCH /api/teams/{id}`).

## Usage walkthrough

1. **Log in** as the seeded SuperAdmin (or create an AuctionAdmin user first).
2. **Admin Dashboard** shows the seeded demo auction. Click **Setup** to review/edit its rules
   (money rules, roster limits, unsold-round settings, visibility, bidding mode). Rules can only
   be edited while the auction is in `Draft` or `Ready` — once `Live`, edits are blocked with a
   409 and you're pointed to the correction workflow.
3. **Teams** page: the demo auction already has 6 teams seeded with balances; add/remove more
   here. Balances shown are always computed from the ledger, not a raw column.
4. **Players** page: 28 demo players are seeded; add more manually or via **CSV Import**
   (columns: `Name,Role,BasePrice,SkillTags,PhotoUrl,AgeOrDob,Notes`, header row skipped).
   Export the current player list to CSV from the same page.
5. Back on the dashboard, click **Mark Ready**, then **Start Live** (requires ≥2 active teams
   and ≥1 player) to move the auction to `Live`.
6. **Live Auction Console** (`live-auction.html`): click **Spin** to have the server randomly
   select the next eligible player (never trusts a client-picked player). Place bids on behalf
   of any team, or use **+Increment** for a quick bid at current-highest + increment. Click
   **Sell** to confirm the sale (writes the Sale record, ledger entry, and audit log atomically,
   then broadcasts to everyone watching), or **Unsold** to send the player to the unsold pool.
7. Open the **Public Display** link (or `display.html?auctionId=<id>`) in another tab/window for
   a fullscreen, read-only view — no login required for public-visibility auctions. It receives
   the same SignalR events and always re-fetches full state via REST on connect/reconnect rather
   than trusting push events alone.
8. Once every player is Sold/Unsold/Withdrawn, the auction automatically flips to
   `MainRoundComplete`. Click **Open Unsold Pool** to move all Unsold players back into
   `ReauctionAvailable` and continue spinning/bidding/selling for that round. Repeat until the
   configured max unsold rounds is reached, after which remaining players become `FinalUnsold`.
9. Click **Complete** to finish the auction (if there are still unresolved unsold-pool players,
   you'll be asked to force-confirm).
10. **Reports** page: rosters per team, full player list with status, bid history, audit log, and
    a **CSV export** link for the whole auction.

Team owners log in and land on **My Team Dashboard**, which shows their balance, roster, and
(if the auction's bidding mode allows team-owner bidding) a bid box for the current live player.

## Architecture notes

- `App.Api` — everything: EF Core entities/DbContext/migrations (`Data/`), business logic
  services (`Services/`), REST controllers (`Controllers/`), the SignalR hub (`Hubs/`), and the
  static frontend (`wwwroot/`).
- `App.Tests` — xUnit tests for the core business logic services (bid validation, ledger
  balance computation, sale/unsold transitions, auction lifecycle guards) against an in-memory
  SQLite database.
- Every multi-table mutation (sell, reverse-sale, balance adjustment, reassignment) runs inside
  an EF Core transaction so the player status, Sale record, ledger entry, and audit log are
  always written together or not at all.
- Team balances are never a mutable column — `TeamLedgerService.GetAvailableBalance` always
  derives the balance from the most recent ledger entry (or the team's opening balance if none
  exist yet).
- JWT bearer auth (custom `Users` table + BCrypt password hashing) with role-based
  `[Authorize(Roles=...)]` plus manual auction-ownership checks on every mutating endpoint.

## What's simplified vs. the full spec

- CSV import is a minimal line-splitter (no quoted-comma handling) — fine for the documented
  simple column format, not a full RFC 4180 parser.
- Category/role roster limits are stored (`AuctionRules.CategoryLimitsJson`) but not yet
  enforced in `BidValidationService` — only overall roster max size and min-remaining-purse are
  enforced.
- No lazy/paged loading — reports and lists load the full data set at once, adequate for the
  target auction sizes (dozens of players/teams) but not built for very large datasets.
- Team-to-owner assignment is done via `PATCH /api/teams/{id}` (no dedicated UI picker yet) —
  there isn't a polished "invite a team owner" flow.
- Photo/logo upload is URL-based only (no file upload/storage pipeline).
