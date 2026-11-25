# Session handover (25 Nov 2026)

> **For Claude in a new session:** read `PROJECT_CONTEXT.md` first, then this file. Talk with Talha in **Roman Urdu**. Continue from "Next steps" below.

## What was done in this session

Branch **`railway-multitenant`** (one commit on top of `main` @ `a1bf5b9`):

1. **Railway deploy**
   - `railway.json`: Dockerfile build, health check `/health`, restart on failure.
   - `docs/DEPLOY_RAILWAY.md`: step-by-step (project + PostgreSQL, variables, domain, Meta webhook).
   - Login keys (ASP.NET Data Protection) are stored in the DB (`DataProtectionKeys` table), so redeploys don't log users out.
2. **Clients (organizations) = multi-restaurant / multi-organization portal**
   - New `Organizations` table. `Restaurants.OrganizationId`; `DashboardUsers.OrganizationId` (was `RestaurantId`).
   - A client login sees only its own organization's restaurants. Checked on the server for every `/api/restaurants/{rid}/...` route (`Dashboard/AccessControl.cs`): another client's restaurant → 403.
   - Suspended client (`IsActive = false`): can't log in (403), open sessions get 401.
   - Migration `20261125051549_Organizations`: each existing restaurant becomes its own client; logins move to it. Tested up and down on real PostgreSQL with data.
3. **Admin sees everything**: new **All clients** page (`dashboard/src/app/pages/clients.ts`, API `Dashboard/ClientEndpoints.cs`):
   - totals + per client/restaurant: today's orders, sales, active orders, chats, last message, logins
   - **+ New client** (business + first restaurant + login in one form), + Add restaurant, + Add login, reset/delete login, rename, suspend/activate, move restaurant, delete empty client, **Open →** a restaurant
   - API: `GET/POST /api/clients`, `PUT/DELETE /api/clients/{id}`, `PUT /api/restaurants/{rid}/client`; `POST /api/users` now takes `organizationId`; `POST /api/restaurants` takes optional `organizationId`.
4. **Simpler UI**: admin lands on All clients; nav: Home, Orders, Chats, Bookings, Customers, Menu & FAQs, Restaurant info, Test the bot; old Users page removed (`/users` → `/clients`); owners don't see AI token stats; topbar shows "Client › Restaurant" for the admin.
5. Tests: `tests/ChatServe.Api.Tests/ClientTests.cs` (new) + `OwnerLoginTests.cs` updated. `dotnet test` → **60 passing** (`KeyedLockTests` is timing-sensitive and occasionally fails on a slow machine; unrelated).
6. Docs updated: `README.md`, `PROJECT_CONTEXT.md`, `docs/HOW_IT_WORKS.md`, `docs/ONBOARDING.md`.

Verified by running the app against local PostgreSQL and checking admin + client logins in a browser (screenshots).

## ❗ Not pushed yet

The cloud session had no GitHub write access to `Haris-1010/ChatServe` (push → 403).
The commit was exported as `railway-multitenant.patch`. To get it into the repo, either:
- give Claude access (https://claude.ai/connect-github → install the Claude GitHub App on the repo), then ask Claude to push `railway-multitenant`; or
- on your PC, in the repo folder:
  ```bash
  git checkout main
  ```
  ```bash
  git checkout -b railway-multitenant
  ```
  ```bash
  git am railway-multitenant.patch
  ```
  ```bash
  git push -u origin railway-multitenant
  ```

**If the patch file is lost** (cloud sessions are temporary): the work only exists in that session's container. A new session would have to redo it from this file's description.

## Running it to try it out

The cloud container can't be opened from your browser (no public URL; tunnels are blocked there). Run it on your PC instead:

```bash
cd dashboard
```
```bash
npm install
```
```bash
npm run build
```
```bash
cd ..
```
```bash
dotnet run --project src/ChatServe.Api --urls http://localhost:5041
```
Open http://localhost:5041 → username `admin`, password `admin` (Development) → **All clients**.
The migration runs automatically on start; existing restaurants each become a client.

Or deploy straight to Railway (`docs/DEPLOY_RAILWAY.md`) and log in at the Railway domain.

## Next steps

1. Get the branch onto GitHub (see above), merge into `main` when happy.
2. Railway: create project + PostgreSQL, set `DATABASE_URL=${{Postgres.DATABASE_URL}}` and `Dashboard__AdminPassword`, generate domain.
3. Dashboard Settings on Railway: WhatsApp permanent token, Phone number ID, Verify token, App secret (signature check ON), Anthropic key. Meta webhook → `https://<domain>/webhook`.
4. Still open from before: subscribe WABA `1812438219994148` to the ChatServe app (`POST /{waba-id}/subscribed_apps`, see `docs/HOW_IT_WORKS.md` §6).
5. Then **Phase 4**: customer labels, no-shows, blacklist.
