# Deploying ChatServe on Railway

One Railway project = the ChatServe app (Docker, API + dashboard on the same URL) + a PostgreSQL database.
`railway.json` in the repo already tells Railway to build the `Dockerfile` and to use `/health` as the health check.

## 1. Create the project (one time)

1. Sign in at [railway.com](https://railway.com) with GitHub. Add a payment method (the Hobby plan is enough to start; the app must not sleep, or WhatsApp webhooks get delayed).
2. **New Project → Deploy from GitHub repo →** pick `Haris-1010/ChatServe`. Choose the branch you want to run (e.g. `main`).
3. In the same project: **+ New → Database → Add PostgreSQL.**
4. Region: in each service's **Settings → Region**, pick the one closest to Pakistan (Southeast Asia / Singapore) for both the app and the database.

## 2. Variables (app service → Variables)

| Variable | Value |
|---|---|
| `DATABASE_URL` | `${{Postgres.DATABASE_URL}}` (Railway's reference to the database; type it exactly like this) |
| `Dashboard__AdminPassword` | A long password for the admin login (username `admin`). **Required**, otherwise login is disabled |
| `Anthropic__ApiKey` | Your Claude API key (can also be set later in the dashboard's Settings) |
| `WhatsApp__AppSecret` | Meta App secret (can also be set later in Settings) |

Everything else (WhatsApp token, Phone number ID, verify token, AI model…) can be set from the dashboard's **Settings** page after the first login. Railway sets `PORT` itself; the app listens on it automatically.

Note the **double underscore** (`__`) in the names.

## 3. Public URL

App service → **Settings → Networking → Generate Domain**. You get something like `https://chatserve-production.up.railway.app`.

- Dashboard: `https://<your-domain>/` → log in as `admin` with `Dashboard__AdminPassword`.
- Webhook for Meta: `https://<your-domain>/webhook` (shown on the Home page's setup checklist too).
- Health check: `https://<your-domain>/health` → `"healthy"`.

A custom domain (e.g. `app.chatserve.pk`) can be added on the same Networking page later.

## 4. After the first deploy

1. Open the dashboard, log in as `admin`.
2. **Settings:** paste the WhatsApp Access token (permanent System User token), Phone number ID, Verify token, App secret; turn the signature check ON. Click **Connection test** and **Claude test**.
3. **Meta:** WhatsApp → Configuration → Webhook → Callback URL = `https://<your-domain>/webhook`, same Verify token → Verify and save → subscribe to `messages`.
4. **All clients → + New client** for each restaurant business (see below).

Database tables are created and upgraded automatically on every start (EF migrations). Login sessions survive redeploys (the login keys are stored in the database).

## 5. Redeploys

Every push to the chosen branch redeploys automatically. Check **Deployments → View logs** if something fails; the app logs `Now listening on: http://0.0.0.0:<port>` when it is up.

## 6. Clients (multi-restaurant / multi-organization)

- A **client** is a business (organization). It can have one or many **restaurants** (branches); each restaurant has its own WhatsApp number (Phone number ID).
- **Admin** (`admin` login) sees every client: the **All clients** page shows each client's restaurants with today's orders, sales and chats, and their logins. **Open →** jumps into any restaurant's orders, chats, menu, etc.
- A **client login** sees only that client's restaurants (a branch switcher appears if it has more than one). Another client's data is blocked on the server (403), not just hidden in the UI.
- **Suspend** a client (e.g. unpaid): its logins stop working immediately. The bot keeps running unless you also turn the bot OFF for its restaurants.

## Moving from Render/Neon

Nothing in the code is Render-specific. To keep existing data, dump the Neon database and restore it into Railway's Postgres (`pg_dump` / `pg_restore` with the two connection strings), then deploy. Otherwise just start fresh; the demo restaurant is created automatically if `Bot:SeedDemoRestaurant` is true and a Phone number ID is set.
