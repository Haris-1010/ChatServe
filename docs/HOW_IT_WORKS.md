# ChatServe: How it works, user journey, and current setup

_Written 24 Nov 2026. Read this first in a new session._

---

## 1. What ChatServe is (in one minute)

ChatServe is a **WhatsApp bot for restaurants**. A customer messages the restaurant's WhatsApp number, and an AI (Claude) answers from the restaurant's menu, takes orders and table bookings, and passes hard cases to staff. The restaurant owner runs everything from a web **dashboard**.

```
Customer's WhatsApp
      │  "How much is the karahi?"
      ▼
Meta WhatsApp Cloud API  ──POST /webhook──►  ChatServe API (.NET, your PC or Render)
                                                  │
                                                  ├─ finds the restaurant (by Phone number ID)
                                                  ├─ saves the message (PostgreSQL)
                                                  ├─ sends menu + last 20 messages to Claude
                                                  ├─ Claude may use tools: place_order, book_table, ...
                                                  └─ sends the reply back through Meta
      ▲                                           │
      └──────────── bot reply on WhatsApp ◄───────┘
```

Three parts:

| Part | What it is | Where |
|---|---|---|
| **API** | ASP.NET Core 8 app: webhook, AI, orders, admin API | `src/ChatServe.Api` |
| **Dashboard** | Angular 22 app, served by the API itself | `dashboard/` (built into `src/ChatServe.Api/wwwroot`) |
| **Database** | PostgreSQL: restaurants, menu, customers, chats, orders, bookings, settings | local Postgres 18 (`chatserve` DB); Neon is ready for the cloud |

---

## 2. Customer journey (on WhatsApp)

1. **Customer sends a message** to the restaurant's WhatsApp number.
2. **Meta calls ChatServe** at `POST /webhook`. ChatServe answers `200 OK` immediately and puts the message in a queue (Meta retries if it waits too long).
3. **Background worker** picks it up:
   - marks it read (blue ticks) and shows "typing…"
   - skips duplicates (Meta sometimes resends)
   - handles one customer's messages one at a time
4. **Find the restaurant** by the `phone_number_id` in the message. No match → ignored (log: "No restaurant configured…").
5. **Checks before AI:**
   - Bot OFF for the restaurant, or paused for this customer (staff took over) → message is saved, no reply.
   - Not text (photo/voice) → fixed "please type your question" reply, no AI cost.
   - Customer typed **"confirm"** (or "1" right after an order summary) → the order is confirmed **without AI**, a ticket goes to the kitchen, fixed reply.
   - **"cancel"** → draft order cancelled.
6. **AI reply:** ChatServe sends Claude the restaurant info, menu (with codes like `[M3]`), deals, FAQs, delivery areas and the last 20 messages. Claude replies in **English by default**, or in the customer's language (e.g. Roman Urdu) if they write in it.
7. **AI tools** Claude can use:
   - `place_order`: creates a **draft** order; the **server** calculates prices and totals (never the AI) and sends a summary: "Reply *confirm* (or 1) to place the order."
   - `get_order_status`: "Where is my order?"
   - `book_table`: saves a booking **request** (staff confirm it later)
   - `handover_to_human`: complaints, refunds, big orders, "talk to a person" → bot pauses for that customer (12 h) and staff get an alert
8. **Reply goes back** through Meta's Graph API to the customer. Every message (customer, bot, staff) is saved in the database.

**Example order flow:**
```
Customer: 1 full chicken karahi and 4 naan, delivery to Johar Town, I'm Ali
Bot:      🧾 Order summary ... Total: Rs 2,940 ... Reply *confirm* (or 1)
Customer: confirm
Bot:      ✅ Thank you Ali! Your order #12 is confirmed ...
Kitchen:  🔔 NEW ORDER #12 (Delivery) ... (WhatsApp to the staff number)
```

---

## 3. Restaurant owner journey (the dashboard)

Open the dashboard → log in. **Admin:** username `admin` (local password `admin`) sees every restaurant. **Restaurant owner:** the username/password the admin created on the **Users** page; they only see their own restaurant. Pages in the left menu:

| Page | What you do there |
|---|---|
| **Overview** | Today's orders, sales, chats; **setup checklist** (what's missing); webhook URL for Meta |
| **Orders** | Live orders (refresh every 10 s). Buttons: Confirm → Preparing → Out for delivery → Completed / Cancel. Optional WhatsApp update to the customer. CSV export |
| **Chats** | Every conversation. Read the chat, **reply as staff** (goes out on WhatsApp), **Pause bot** ("I'll reply") or **Turn bot back on** |
| **Table bookings** | Booking requests from the bot: Confirm / Cancel (optional WhatsApp to customer) |
| **Customers** | Everyone who messaged: orders, spending, last message; pause/resume bot per customer; CSV |
| **Menu, deals, FAQs** | The bot's knowledge: dishes + prices (+ available on/off), deals, FAQs, **delivery areas + charges**. Changes apply from the next message |
| **Restaurant info** | Bot ON/OFF, name, **WhatsApp Phone number ID**, kitchen/staff WhatsApp number, address, timings, minimum order, payment methods, bot tone. Also "New restaurant" (one app can run many restaurants) |
| **Bot test (simulator)** | Chat with the bot like a customer **without WhatsApp**. Test numbers start with `000`; nothing is sent to real WhatsApp. Kitchen messages show in a yellow card |
| **Users** (admin only) | Create a login for each restaurant owner (username + password + restaurant), reset a password, delete a login. A reset or delete logs the owner out immediately |
| **My account** (owners only) | Change your own password |
| **Settings** (admin only) | WhatsApp keys (Phone number ID, Access token, Verify token, App secret), Anthropic key, AI model, order rules, dashboard password. Saved in the DB, applied without restart. "Connection test" and "Claude test" buttons |

**Typical day for the owner:** check Overview → watch Orders and move them through the statuses → answer tricky Chats (pause the bot while talking) → confirm Table bookings → update Menu when something runs out.

**The `/chats` page** (e.g. `https://<public-link>/chats`): left = list of conversations (newest first; "Staff" tag = bot paused, "Test" tag = simulator chat). Click one → right side shows the whole thread (🤖 bot, 👤 staff, customer). At the bottom you can type a staff reply; "Pause bot" is ticked by default so the bot stays quiet while you talk.

---

## 4. Current setup (as of 24 Nov 2026)

### Running on this PC (started from the Claude session; restart them yourself after a reboot)
| What | Command | Address |
|---|---|---|
| ChatServe app | `cd "D:\Development\New folder\ChatServe"; dotnet run --project src/ChatServe.Api --urls http://localhost:5041` (after a dashboard change, run `npm run build` in `dashboard\` first) | http://localhost:5041 |
| Cloudflare tunnel for ChatServe | `& "C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel --url http://localhost:5041` | random `https://….trycloudflare.com` (shown in its output) — **changes every restart** |
| ngrok for the other app (port 44395) | `ngrok http https://localhost:44395 --host-header=rewrite` | `your fixed ngrok-free.dev address` |

Port 5040 is taken by a Windows service, so ChatServe runs on **5041**.

### Code
- Repo: `Haris-1010/ChatServe` (public). Branch **`render-deploy`** has the dashboard, English, fixes, `render.yaml`, `PRIVACY.md`. Branch **`owner-logins`** (local, on top of it) adds restaurant owner logins. `main` is still the old initial commit.
- The running app now uses the repo folder itself: `D:\Development\New folder\ChatServe`. The older copy in `ChatServe-dashboard\` is no longer used.
- Tests: `dotnet test` → 57 passing.

### Meta / WhatsApp
- Meta app: **ChatServe**, App ID `1385105803536953`, business portfolio **ChatServe** (unverified), app **Published**.
- Test number **+1 555 194 7714**, Phone number ID `1379398901934597`, WhatsApp Business Account ID `1812438219994148`.
- ChatServe Settings: Phone number ID ✅, Access token ✅ (temporary, **expires every 24 h**), Verify token ✅, Graph API version `v25.0`. Connection test passes.
- Restaurant **Talha's Karahi** is linked to the test number (demo menu: 17 dishes, 2 deals, 3 FAQs, 5 delivery areas).
- Webhook in Meta: Callback URL = Cloudflare link + `/webhook`, verified ✅, `messages` field subscribed ✅.
- Privacy policy: https://github.com/Haris-1010/ChatServe/blob/render-deploy/PRIVACY.md

### Cloud (prepared, not deployed)
- **Neon** project `chatserve` (AWS Singapore) is created. Its connection string goes into Render's `DATABASE_URL`.
- **Render**: needs a card for verification even on the free plan, so nothing is deployed. Settings to use: Docker, branch `render-deploy`, region Singapore, plan Free, health check `/health`, env vars `DATABASE_URL`, `Dashboard__AdminPassword`, `Anthropic__ApiKey`.

---

## 5. Multiple restaurants

Full go-live and shop onboarding checklist (what to take from a shop, what to give, pricing): see `docs/ONBOARDING.md`.

One ChatServe app runs many restaurants. Each restaurant needs **its own WhatsApp number** (its own Phone number ID); incoming messages are routed by that ID.

To add a restaurant:
1. **Meta:** WhatsApp Manager (your ChatServe business portfolio) → Phone numbers → Add phone number → verify by SMS/call → copy the new **Phone number ID**. The number must not be active in the normal WhatsApp app.
2. **Dashboard (admin):** Restaurant info → New restaurant (name + Phone number ID) → fill in info, menu, delivery areas, staff number → test in the simulator.
3. **Dashboard (admin):** Users → New owner login for that restaurant → share the username/password with the owner.

Limits today: one Meta access token for all numbers (all numbers must be under your WhatsApp Business Account); restaurants can't connect their own Meta account themselves (needs Meta Embedded Signup / Tech Provider).

## 6. Open problem and next steps

### ❗ Real WhatsApp messages are not reaching ChatServe yet
A test message was sent from a phone to +1 555 194 7714, but no message arrived at `/webhook` (only Meta's verification calls did).

**Cause found:** the WhatsApp Business Account `1812438219994148` is subscribed only to Meta's own test app ("WA DevX Webhook Events 1P App"), **not to the ChatServe app**, so Meta doesn't forward messages to our webhook.

**Fix (not done yet, waiting for your OK):** subscribe the account to the ChatServe app with one call, using the access token from Settings:
```
POST https://graph.facebook.com/v25.0/1812438219994148/subscribed_apps
Authorization: Bearer <access token>
```
Then check with `GET .../1812438219994148/subscribed_apps` (ChatServe should be listed) and send the test message again. Watch the app log for `Processing text message … from …` and `AI reply for Talha's Karahi`.

### Then
1. Make sure your phone number is in Meta's **To** list (Step 1. Try it out).
2. Every day: generate a new Meta access token and paste it in Settings (or create a permanent System User token, see README §7).
3. If the PC restarts: start the app and the Cloudflare tunnel again, then update the Callback URL in Meta (Use cases → Customize → Step 2. Production setup → Configure Webhooks), using the same Verify token.
4. Before going live: change the dashboard password, add the Meta **App secret** and turn the signature check ON, use a permanent token, and deploy (Render + Neon) so it doesn't depend on your PC.
5. Merge `render-deploy` into `main` when you're happy with it.

### Secrets
No passwords or tokens are written in this file. They live in ChatServe **Settings** (local database), in Render/Neon, or in your Meta account.
