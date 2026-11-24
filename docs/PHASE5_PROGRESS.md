# Phase 5 Progress: Dashboard (Angular) + admin API

> Naye session ke liye: pehle `PROJECT_CONTEXT.md`, phir ye file. Jo ✅ hai use dobara mat banao.

## Faisle
| Cheez | Faisla |
|---|---|
| Frontend | **Angular 22** (standalone, zoneless, signals), `dashboard/` folder. Koi UI library nahi, ek global `styles.css` (light + dark, mobile) |
| Serve | `npm run build` output `src/ChatServe.Api/wwwroot` mein. API khud dashboard serve karti hai (ek URL). Vercel ke liye `API_BASE` + `Dashboard:AllowedOrigins` |
| Auth | ASP.NET Core **bearer token** (`AddBearerToken`, 12 ghante). Ek admin password `Dashboard:AdminPassword` (Development default `admin`, Production mein zaroori) |
| Settings | Nayi `AppSettings` table (key/value). `DashboardSettingsProvider` configuration mein sab se aakhir mein, is liye env/appsettings par tarjeeh. Save par reload → `IOptionsMonitor` foran nayi value. Connection string UI se nahi |
| Runtime switch | `Bot:UseAi` ab har message par (`SwitchingReplyGenerator`). Anthropic key badle to `AnthropicClientProvider` naya client banata hai. WhatsApp tokens ke baghair bhi app start hoti hai |
| Secrets | API secrets kabhi wapas nahi bhejti, sirf `••••abcd` hint |
| Simulator | `POST /api/restaurants/{id}/simulator`: asli `IReplyGenerator` (DB, tools, orders sab asli). Number `000…` = test customer: staff alerts `SimulationContext` mein jama, staff/order messages WhatsApp par nahi jate |
| Staff reply | Chats page se WhatsApp text (restaurant ke number se), `ChatRole.Staff` save. Option: bot pause (`HandoverPauseHours`) |
| Order status | Dashboard se Confirmed → Preparing → OutForDelivery → Completed / Cancelled, optional customer ko WhatsApp (24h window ke andar) |

## Endpoints (`/api`, login zaroori)
`auth/login`, `status`, `settings` (GET/PUT), `settings/test/whatsapp`, `settings/test/ai`,
`restaurants` (GET/POST), `restaurants/{rid}` (GET/PUT), `…/menu|deals|faqs|zones` (CRUD),
`…/overview`, `…/orders` (+ `/{id}/status`), `…/bookings` (+ `/{id}/status`), `…/customers` (+ `/pause`, `/resume`, `/messages` GET/POST),
`…/conversations`, `…/simulator`.

## Checklist
- [x] AppSettings migration (`DashboardSettings`)
- [x] Admin API + 6 tests (login, secrets hidden, settings foran lagu, simulator AI/hello switch, simulator order → orders page, staff reply + pause, menu CRUD + tenant isolation). Kul 50 tests pass
- [x] Angular pages: Login, Overview (setup checklist + webhook URL), Orders, Chats, Bookings, Customers, Menu/Deals/FAQs/Zones, Restaurant info (+ naya restaurant), Settings (+ connection tests), Simulator
- [x] Local Postgres par Playwright se har page aur actions check (desktop + mobile, console errors nahi)
- [x] Dockerfile: Node stage dashboard build karta hai
- [ ] Docker image ka build (is environment mein Docker daemon nahi tha)
- [ ] Asli Claude key aur Meta number ke saath dashboard se test
- [ ] Phase 4 ke baad: customer labels, no-show button, blacklist dashboard par
- [ ] Har restaurant owner ka alag login (multi-tenant SaaS), abhi ek admin
