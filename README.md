# ChatServe API

Restaurants ke liye WhatsApp AI auto-reply agent.
- **Phase 1 ✅** Meta webhook (verify + receive), Graph API reply
- **Phase 2 ✅** Claude (Haiku 5.5) restaurant ke menu/settings/FAQs se jawab deta hai, chat memory PostgreSQL mein
- **Phase 3 ✅** Order flow: AI tools (order draft, status, table booking, human handover), customer ka "confirm", kitchen ko WhatsApp ticket

Progress notes: `PROJECT_CONTEXT.md`, `docs/PHASE2_PROGRESS.md`, `docs/PHASE3_PROGRESS.md`.

```
Customer (WhatsApp) → Meta Cloud API → POST /webhook → queue → MessageProcessingService
   → (blue tick + typing...) → ClaudeReplyGenerator (DB: menu + aakhri 20 messages → Claude) → Graph API → customer ko jawab
```

## Project structure

```
ChatServe/
├── ChatServe.sln
├── Dockerfile
├── src/ChatServe.Api/
│   ├── Program.cs                         DI, options validation, PORT env (Railway/Render)
│   ├── Configuration/WhatsAppOptions.cs   WhatsApp + Bot settings
│   ├── Controllers/WebhookController.cs   GET verify, POST receive (signature check, foran 200)
│   ├── WhatsApp/
│   │   ├── WhatsAppClient.cs              Graph API (HttpClient): SendText, MarkAsRead + typing
│   │   ├── WebhookSignatureValidator.cs   X-Hub-Signature-256 (HMAC-SHA256, App Secret)
│   │   └── Models/WebhookPayload.cs       Meta payload ke DTOs
│   └── Messaging/
│       ├── WebhookParser.cs               payload → IncomingMessage (status updates ignore)
│       ├── MessageQueue.cs                in-memory Channel queue
│       ├── MessageProcessingService.cs    background worker, duplicate (Meta retry) protection
│       └── ReplyGenerator.cs              IReplyGenerator; Phase 1 = HelloReplyGenerator
└── tests/ChatServe.Api.Tests/             44 tests (parser, signature, AI reply, order flow, endpoint end-to-end)
```

Phase 2 ki files: `Data/` (EF Core entities, DbContext, migrations, demo seed), `Ai/` (Claude client, system prompt), `Messaging/ClaudeReplyGenerator.cs`, `Messaging/KeyedLock.cs` (ek customer ke messages ek ke baad ek).

Phase 3 ki files: `Orders/` (OrderService: validation + totals, RestaurantTools: AI tools + executor, OrderFormatter: summary/ticket/confirm pehchanna, StaffNotifier: kitchen ko WhatsApp). Tool loop `Ai/ChatCompletionClient.cs` mein.

## Settings

| Key | Kahan se milega |
|---|---|
| `WhatsApp:VerifyToken` | Apni marzi ka lamba random string. Yahi Meta dashboard mein daalna hai |
| `WhatsApp:AccessToken` | WhatsApp > API Setup (temporary, 24 ghante). Live ke liye System User token |
| `WhatsApp:PhoneNumberId` | WhatsApp > API Setup > "Phone number ID" (number khud nahi, ID) |
| `WhatsApp:AppSecret` | App settings > Basic > App secret ("Show") |
| `WhatsApp:ValidateSignature` | Production mein `true` (default). Development mein `false` hai |
| `WhatsApp:GraphApiVersion` | Default `v23.0`. Dashboard ke API Setup wale curl example mein jo version ho, woh rakh lein |
| `ConnectionStrings:Default` | PostgreSQL. Railway/Render par `DATABASE_URL` bhi chalta hai (postgres:// format) |
| `Anthropic:ApiKey` | console.anthropic.com > API Keys (ya `ANTHROPIC_API_KEY` env var) |
| `Anthropic:Model` | Default `claude-haiku-5-5` |
| `Anthropic:Effort` | Default `low` (chat ke liye kaafi, sasta aur tez). Jawab kamzor lagen to `medium` |
| `Anthropic:HistoryLimit` | Har customer ki kitni pichli messages AI ko yaad rahen (default 20) |
| `Anthropic:MaxToolRounds` | Ek message par AI kitni dafa tools chala sakta hai (default 5) |
| `Orders:DraftExpiryMinutes` | "confirm" ka kitni der intezar (default 180) |
| `Orders:HandoverPauseHours` | Handover ke baad bot us customer ke liye kitne ghante chup (default 12) |
| `Bot:DemoStaffWhatsAppNumber` | Demo restaurant ka kitchen/staff number, maslan `923001234567`. Confirmed orders, bookings, handover yahan aate hain |
| `Bot:UseAi` | `false` = Phase 1 wala simple hello reply (AI kharcha zero) |
| `Bot:SeedDemoRestaurant` | `true` = configured `PhoneNumberId` par "Talha's Karahi" demo menu khud ban jata hai |

Secrets kabhi `appsettings.json` mein commit na karein.

**Local (user-secrets):**
```bash
cd src/ChatServe.Api
dotnet user-secrets set "WhatsApp:VerifyToken" "chatserve-verify-apna-random-string"
dotnet user-secrets set "WhatsApp:AccessToken" "EAAG...."
dotnet user-secrets set "WhatsApp:PhoneNumberId" "1234567890"
dotnet user-secrets set "WhatsApp:AppSecret" "abc123...."
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-...."
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=chatserve;Username=postgres;Password=apna-password"
```

**PostgreSQL local (Windows):** postgresql.org se installer, ya Docker:
```bash
docker run -d --name chatserve-db -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16
```
App start hote hi tables khud ban jati hain (EF migrations) aur demo restaurant seed ho jata hai. Naya migration: `dotnet ef migrations add <Naam> -p src/ChatServe.Api -o Data/Migrations`.

**Anthropic:** console.anthropic.com par account > Billing mein credits (international card) > API Keys > Create key. Claude Pro plan ka API se taluq nahi, billing alag hai.

**Server (Railway/Render env vars):** `WhatsApp__VerifyToken`, `WhatsApp__AccessToken`, `WhatsApp__PhoneNumberId`, `WhatsApp__AppSecret`, `Anthropic__ApiKey`, `DATABASE_URL` (double underscore).

## Local chalana

```bash
dotnet run --project src/ChatServe.Api      # http://localhost:5040
dotnet test                                  # 44 tests
```

Check: `http://localhost:5040/health` → `"healthy"`

---

## Meta Developer App setup (step by step)

### 1. Business portfolio + App
1. [business.facebook.com](https://business.facebook.com) par Business portfolio banayein (naam: ChatServe ya apna business).
2. [developers.facebook.com](https://developers.facebook.com) > **My Apps > Create App**.
3. Use case mein **"Connect with customers through WhatsApp"** chunein (purane UI mein: type **Business**).
4. App ko apne Business portfolio se link karein aur create kar dein.

### 2. Test number aur pehla message
1. Left menu: **WhatsApp > API Setup**.
2. Yahan milega: **Temporary access token**, Meta ka **test phone number**, **Phone number ID**, **WhatsApp Business Account ID**. Phone number ID copy kar lein.
3. **"To"** field mein apna personal WhatsApp number add karein (`+92 3xx xxxxxxx`). Code aayega, verify karein. Test mode mein sirf yahi (max 5) numbers par message ja sakta hai.
4. **"Send message"** dabayein. Aap ke phone par `hello_world` template aana chahiye. Ye aa gaya to account theek hai.

### 3. App Secret
**App settings > Basic > App secret > Show** → copy karke `WhatsApp:AppSecret` mein daalein.

### 4. Public URL (ngrok ya Dev Tunnels)
Meta ko HTTPS public URL chahiye.

```bash
dotnet run --project src/ChatServe.Api
```
```bash
ngrok http 5040
```
ngrok `https://xxxx.ngrok-free.app` dega. (Visual Studio wale: Dev Tunnels, access **Public** rakhein.)

> Note: ngrok free ka URL har dafa badalta hai, phir Meta mein bhi badalna padega. ngrok ka free static domain le lein to aasani rahegi.

### 5. Webhook connect karna
1. **WhatsApp > Configuration > Webhook > Edit**.
2. **Callback URL:** `https://xxxx.ngrok-free.app/webhook`
3. **Verify token:** wohi jo `WhatsApp:VerifyToken` mein hai.
4. **Verify and save.** App ke log mein `Webhook verified by Meta` aayega.
5. Neeche **Webhook fields > `messages` > Subscribe** zaroor karein. Ye bhool gaye to messages nahi aayenge.

### 6. Test
Apne phone se **test number par** bhejein: "Karahi kitne ki hai?", "DHA mein delivery hai?", "Family deal mein kya hai?". Bot blue tick + typing dikhayega aur demo menu se jawab dega. Saari chat `ChatMessages` table mein (token usage ke saath) save hoti hai.

Customer pehle message karta hai, is liye 24 ghante ki window khul jaati hai aur free-form reply ja sakta hai.

### Aam masle

| Masla / error | Wajah aur hal |
|---|---|
| Verify fail (403) | Verify token match nahi kar raha, ya app chal nahi rahi / ngrok band hai |
| Messages aa nahi rahe | Webhook fields mein `messages` subscribe nahi kiya |
| 401 log: "invalid signature" | `AppSecret` ghalat hai (Development mein signature check band hota hai) |
| Error `190` | Access token expire (temporary token 24 ghante ka hota hai). Naya lein ya System User token |
| Error `131030` | Recipient number "To" allowed list mein nahi (test mode) |
| Error `131047` | 24 ghante ki window khatam, ab sirf approved template ja sakta hai |
| Error `100` / invalid parameter | Phone number ID ghalat (number nahi, ID daalni hai) ya Graph version purana |

### 7. Permanent token (live karne se pehle)
1. **Business Settings > Users > System users > Add**, role **Admin**.
2. **Assign assets**: apni App (full control) aur WhatsApp account (full control).
3. **Generate new token** > app chunein > expiry **Never** > permissions: `whatsapp_business_messaging`, `whatsapp_business_management`.
4. Ye token `WhatsApp__AccessToken` env var mein (kabhi git mein nahi).

---

## Docker

```bash
docker build -t chatserve-api .
```
```bash
docker run -p 8080:8080 -e WhatsApp__VerifyToken=... -e WhatsApp__AccessToken=... -e WhatsApp__PhoneNumberId=... -e WhatsApp__AppSecret=... chatserve-api
```
Railway/Render `PORT` env var dete hain, app khud usi port par sunti hai.

## Abhi ki hudood (jaan boojh kar)
- Queue in-memory hai: restart ke waqt queue mein pade (chand second ke) messages ja sakte hain.
- Duplicate check memory + DB (`WhatsAppMessageId`) dono se hota hai. Customer lock ek server instance tak hai (multiple instances par Phase 6 mein dekhenge).
- Abhi sirf text samajhta hai; image/voice par AI call ke baghair "sirf text" wala jawab.
- Kitchen ticket free-form WhatsApp text hai: Meta sirf tab deliver karta hai jab staff number ne pichhle 24 ghante mein business number ko message kiya ho. Staff roz shift ke shuru mein "start" bhej de. Pakka hal: Phase 7 mein utility template. Order DB mein har haal mein save hota hai.
- Bot ke liye "un-pause" (handover khatam) abhi sirf waqt guzarne se (`HandoverPauseHours`). Dashboard se Phase 5 mein.
- Order status (Preparing, OutForDelivery) badalna aur customer ko update bhejna Phase 5 (dashboard) + Phase 7 (templates) mein.
- Restaurant ka data abhi seed/DB se; dashboard Phase 5 mein. Abhi ek hi `AccessToken`.
