# ChatServe API

A WhatsApp AI auto-reply agent for restaurants.
- **Phase 1 ✅** Meta webhook (verify + receive), Graph API reply
- **Phase 2 ✅** Claude (Haiku 5.5) answers from the restaurant's menu/settings/FAQs, with chat memory in PostgreSQL
- **Phase 3 ✅** Order flow: AI tools (order draft, status, table booking, human handover), customer "confirm", WhatsApp ticket to the kitchen
- **Phase 5 ✅ (dashboard)** Angular dashboard: all settings, menu/deals/FAQs/delivery areas, live orders, chats (staff reply, bot pause), bookings, customers (CSV), and a **bot simulator** that works without WhatsApp

## Quick start (with the dashboard)

Requirements: .NET 8 SDK, Node.js 20+ (22 recommended), PostgreSQL (see the Docker command below).

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
dotnet run --project src/ChatServe.Api
```

Open **http://localhost:5040** in your browser. Password (Development): `admin`. After logging in:
1. **Settings**: enter WhatsApp (Phone number ID, Access token, Verify token, App secret) and the Anthropic API key, click **Save**, then "Connection test" / "Claude test". No restart needed. Change the **Dashboard password** here too.
2. **Menu, deals, FAQs** and **Restaurant info**: the bot answers from these.
3. **Bot test (simulator)**: chat with the bot as a customer without WhatsApp (order, confirm, booking). Test chat numbers start with `000`; nothing is sent to real WhatsApp.
4. **Overview** shows a setup checklist of what is still missing, and the webhook URL for Meta.

> If port 5040 is already in use on Windows (the Connected Devices Platform service sometimes takes it), run on another port: `dotnet run --project src/ChatServe.Api --urls http://localhost:5041`.

While working on the dashboard (hot reload): keep the API running with `dotnet run`, and in a second terminal run `cd dashboard` then `npm start` → http://localhost:4200 (API calls are proxied to 5040).

Progress notes: `PROJECT_CONTEXT.md`, `docs/PHASE2_PROGRESS.md`, `docs/PHASE3_PROGRESS.md`, `docs/PHASE5_PROGRESS.md`.

```
Customer (WhatsApp) → Meta Cloud API → POST /webhook → queue → MessageProcessingService
   → (blue tick + typing...) → ClaudeReplyGenerator (DB: menu + last 20 messages → Claude) → Graph API → reply to the customer
```

## Project structure

```
ChatServe/
├── ChatServe.sln
├── Dockerfile
├── dashboard/                             Angular dashboard (builds into src/ChatServe.Api/wwwroot)
├── src/ChatServe.Api/
│   ├── Program.cs                         DI, options validation, PORT env (Railway/Render)
│   ├── Configuration/WhatsAppOptions.cs   WhatsApp + Bot settings
│   ├── Controllers/WebhookController.cs   GET verify, POST receive (signature check, immediate 200)
│   ├── WhatsApp/
│   │   ├── WhatsAppClient.cs              Graph API (HttpClient): SendText, MarkAsRead + typing
│   │   ├── WebhookSignatureValidator.cs   X-Hub-Signature-256 (HMAC-SHA256, App Secret)
│   │   └── Models/WebhookPayload.cs       DTOs for the Meta payload
│   └── Messaging/
│       ├── WebhookParser.cs               payload → IncomingMessage (status updates ignored)
│       ├── MessageQueue.cs                in-memory Channel queue
│       ├── MessageProcessingService.cs    background worker, duplicate (Meta retry) protection
│       └── ReplyGenerator.cs              IReplyGenerator; Phase 1 = HelloReplyGenerator
└── tests/ChatServe.Api.Tests/             50 tests (parser, signature, AI reply, order flow, dashboard API, endpoint end-to-end)
```

Phase 2 files: `Data/` (EF Core entities, DbContext, migrations, demo seed), `Ai/` (Claude client, system prompt), `Messaging/ClaudeReplyGenerator.cs`, `Messaging/KeyedLock.cs` (one customer's messages are processed one at a time).

Phase 3 files: `Orders/` (OrderService: validation + totals, RestaurantTools: AI tools + executor, OrderFormatter: summary/ticket/recognising "confirm", StaffNotifier: WhatsApp to the kitchen). The tool loop is in `Ai/ChatCompletionClient.cs`.

Phase 5 files: `Dashboard/` (login, settings, restaurant/menu and operations endpoints), `Configuration/DashboardSettings.cs` (settings stored in the DB).

## Settings

All the settings below (except the connection string) can also be set from the **dashboard's Settings page**. A value saved there goes to the DB (`AppSettings` table), applies immediately, and takes priority over appsettings/user-secrets/env vars. "Reset to default" removes the dashboard value.

| Key | Where to get it |
|---|---|
| `WhatsApp:VerifyToken` | Any long random string you choose. Enter the same value in the Meta dashboard |
| `WhatsApp:AccessToken` | WhatsApp > API Setup (temporary, 24 hours). For going live, a System User token |
| `WhatsApp:PhoneNumberId` | WhatsApp > API Setup > "Phone number ID" (the ID, not the phone number itself) |
| `WhatsApp:AppSecret` | App settings > Basic > App secret ("Show") |
| `WhatsApp:ValidateSignature` | `true` in production (default). `false` in Development |
| `WhatsApp:GraphApiVersion` | Default `v23.0`. Use the version shown in the curl example on the API Setup page |
| `ConnectionStrings:Default` | PostgreSQL. On Railway/Render, `DATABASE_URL` also works (postgres:// format) |
| `Anthropic:ApiKey` | console.anthropic.com > API Keys (or the `ANTHROPIC_API_KEY` env var) |
| `Anthropic:Model` | Default `claude-haiku-5-5` |
| `Anthropic:Effort` | Default `low` (enough for chat, cheaper and faster). Use `medium` if replies seem weak |
| `Anthropic:HistoryLimit` | How many past messages per customer the AI remembers (default 20) |
| `Anthropic:MaxToolRounds` | How many times the AI can use tools for one message (default 5) |
| `Orders:DraftExpiryMinutes` | How long to wait for "confirm" (default 180) |
| `Orders:HandoverPauseHours` | After a handover, how many hours the bot stays quiet for that customer (default 12) |
| `Bot:DemoStaffWhatsAppNumber` | Kitchen/staff number for the demo restaurant, e.g. `923001234567`. Confirmed orders, bookings and handovers are sent here |
| `Bot:UseAi` | `false` = the simple Phase 1 hello reply (no AI cost) |
| `Bot:SeedDemoRestaurant` | `true` = the "Talha's Karahi" demo menu is created automatically for the configured `PhoneNumberId` (on app start) |
| `Dashboard:AdminPassword` | The dashboard password. In Development it defaults to `admin`. **Required in production** (env var `Dashboard__AdminPassword`), otherwise login is disabled |
| `Dashboard:AllowedOrigins` | Only when the dashboard is on a different domain (e.g. Vercel): `https://xyz.vercel.app`. Also set `API_BASE` in `dashboard/src/environments.ts` to the backend URL |

Never commit secrets to `appsettings.json`.

**Local (user-secrets):**
```bash
cd src/ChatServe.Api
dotnet user-secrets set "WhatsApp:VerifyToken" "chatserve-verify-your-random-string"
dotnet user-secrets set "WhatsApp:AccessToken" "EAAG...."
dotnet user-secrets set "WhatsApp:PhoneNumberId" "1234567890"
dotnet user-secrets set "WhatsApp:AppSecret" "abc123...."
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-...."
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=chatserve;Username=postgres;Password=your-password"
```

**Local PostgreSQL (Windows):** use the installer from postgresql.org, or Docker:
```bash
docker run -d --name chatserve-db -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16
```
When the app starts, the tables are created automatically (EF migrations) and the demo restaurant is seeded. New migration: `dotnet ef migrations add <Name> -p src/ChatServe.Api -o Data/Migrations`.

**Anthropic:** create an account on console.anthropic.com > add credits under Billing (international card) > API Keys > Create key. A Claude Pro plan does not include API usage; API billing is separate.

**Server (Railway/Render env vars):** `Dashboard__AdminPassword` (required), `WhatsApp__VerifyToken`, `WhatsApp__AccessToken`, `WhatsApp__PhoneNumberId`, `WhatsApp__AppSecret`, `Anthropic__ApiKey`, `DATABASE_URL` (note the double underscore).

## Running locally

```bash
dotnet run --project src/ChatServe.Api      # http://localhost:5040
dotnet test                                  # 50 tests
```

Check: `http://localhost:5040/health` → `"healthy"`

---

## Meta Developer App setup (step by step)

### 1. Business portfolio + App
1. Create a Business portfolio on [business.facebook.com](https://business.facebook.com) (name: ChatServe or your business).
2. [developers.facebook.com](https://developers.facebook.com) > **My Apps > Create App**.
3. For the use case, choose **"Connect with customers through WhatsApp"** (in the older UI: type **Business**).
4. Link the app to your Business portfolio and create it.

### 2. Test number and first message
1. Left menu: **WhatsApp > API Setup**.
2. Here you'll find: the **Temporary access token**, Meta's **test phone number**, the **Phone number ID** and the **WhatsApp Business Account ID**. Copy the Phone number ID.
3. Add your personal WhatsApp number in the **"To"** field (`+92 3xx xxxxxxx`). You'll get a code; verify it. In test mode, messages can only go to these numbers (max 5).
4. Click **"Send message"**. The `hello_world` template should arrive on your phone. If it does, the account is working.

### 3. App Secret
**App settings > Basic > App secret > Show** → copy it into `WhatsApp:AppSecret`.

### 4. Public URL (ngrok or Dev Tunnels)
Meta needs a public HTTPS URL.

```bash
dotnet run --project src/ChatServe.Api
```
```bash
ngrok http 5040
```
ngrok will give you `https://xxxx.ngrok-free.app`. (Visual Studio users: Dev Tunnels, with access set to **Public**.)

> Note: the free ngrok URL changes every time, and then you have to update it in Meta too. A free ngrok static domain makes this easier.

### 5. Connect the webhook
1. **WhatsApp > Configuration > Webhook > Edit**.
2. **Callback URL:** `https://xxxx.ngrok-free.app/webhook`
3. **Verify token:** the same value as `WhatsApp:VerifyToken`.
4. **Verify and save.** The app log will show `Webhook verified by Meta`.
5. Below that, make sure to **Subscribe** to **Webhook fields > `messages`**. If you forget, no messages will arrive.

### 6. Test
From your phone, message the **test number**: "How much is the karahi?", "Do you deliver to DHA?", "What's in the family deal?". The bot shows a blue tick + typing and replies from the demo menu. The whole chat is saved in the `ChatMessages` table (with token usage).

Because the customer messages first, the 24-hour window opens and free-form replies can be sent.

### Common problems

| Problem / error | Cause and fix |
|---|---|
| Verify fails (403) | The verify token doesn't match, or the app / ngrok isn't running |
| No messages arriving | `messages` isn't subscribed under Webhook fields |
| 401 log: "invalid signature" | Wrong `AppSecret` (the signature check is off in Development) |
| Error `190` | Access token expired (temporary tokens last 24 hours). Get a new one or use a System User token |
| Error `131030` | The recipient number isn't in the "To" allowed list (test mode) |
| Error `131047` | The 24-hour window has closed; only an approved template can be sent now |
| Error `100` / invalid parameter | Wrong Phone number ID (use the ID, not the number) or an old Graph version |

### 7. Permanent token (before going live)
1. **Business Settings > Users > System users > Add**, role **Admin**.
2. **Assign assets**: your App (full control) and WhatsApp account (full control).
3. **Generate new token** > choose the app > expiry **Never** > permissions: `whatsapp_business_messaging`, `whatsapp_business_management`.
4. Put this token in the `WhatsApp__AccessToken` env var (never in git).

---

## Docker

```bash
docker build -t chatserve-api .
```
```bash
docker run -p 8080:8080 -e WhatsApp__VerifyToken=... -e WhatsApp__AccessToken=... -e WhatsApp__PhoneNumberId=... -e WhatsApp__AppSecret=... chatserve-api
```
Railway/Render provide a `PORT` env var and the app listens on that port automatically. The Dockerfile also builds the dashboard, so after deploying, the dashboard is served at the same URL.

## Current limitations (by design)
- The queue is in-memory: messages sitting in the queue (for a few seconds) can be lost on restart.
- Duplicate checks use both memory and the DB (`WhatsAppMessageId`). The customer lock only works within one server instance (multiple instances will be handled in Phase 6).
- Only text is understood for now; images/voice get a "text only" reply without an AI call.
- The kitchen ticket is free-form WhatsApp text: Meta only delivers it if the staff number has messaged the business number in the last 24 hours. Staff should send "start" at the beginning of each shift. Proper fix: a utility template in Phase 7. The order is always saved in the DB.
- The bot "un-pauses" after a handover either when time passes (`HandoverPauseHours`) or from the Chats page in the dashboard.
- Sending order status updates (Preparing, OutForDelivery) to customers outside the 24-hour window needs templates (Phase 7).
- There is only one `AccessToken` (for all restaurants) and one dashboard admin password (separate logins per restaurant owner will come later).
- The dashboard login token can expire when the server restarts (just log in again).
- A new `PhoneNumberId` saved in Settings does not create the demo restaurant until the app restarts. Or create a restaurant / change the ID on the Restaurant info page.
