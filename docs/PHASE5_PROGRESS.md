# Phase 5 Progress: Dashboard (Angular) + admin API

> For a new session: read `PROJECT_CONTEXT.md` first, then this file. Don't rebuild anything marked ✅.

## Decisions
| Item | Decision |
|---|---|
| Frontend | **Angular 22** (standalone, zoneless, signals) in the `dashboard/` folder. No UI library, one global `styles.css` (light + dark, mobile) |
| Serving | `npm run build` outputs to `src/ChatServe.Api/wwwroot`. The API serves the dashboard itself (one URL). For Vercel: `API_BASE` + `Dashboard:AllowedOrigins` |
| Auth | ASP.NET Core **bearer token** (`AddBearerToken`, 12 hours). One admin password `Dashboard:AdminPassword` (Development default `admin`, required in Production) |
| Settings | New `AppSettings` table (key/value). `DashboardSettingsProvider` is added to configuration last, so it wins over env/appsettings. On save it reloads → `IOptionsMonitor` sees the new value immediately. The connection string isn't editable from the UI |
| Runtime switch | `Bot:UseAi` is now checked on every message (`SwitchingReplyGenerator`). When the Anthropic key changes, `AnthropicClientProvider` builds a new client. The app starts even without WhatsApp tokens |
| Secrets | The API never returns secrets, only a `••••abcd` hint |
| Simulator | `POST /api/restaurants/{id}/simulator`: uses the real `IReplyGenerator` (DB, tools, orders are all real). Number `000…` = test customer: staff alerts are collected in `SimulationContext`, and staff/order messages are not sent on WhatsApp |
| Staff reply | WhatsApp text from the Chats page (from the restaurant's number), saved as `ChatRole.Staff`. Option: pause the bot (`HandoverPauseHours`) |
| Order status | From the dashboard: Confirmed → Preparing → OutForDelivery → Completed / Cancelled, with an optional WhatsApp to the customer (inside the 24h window) |
| Language | The whole product is English by default: dashboard, settings, customer/kitchen messages, demo data, docs and code comments. The AI replies in English unless the customer writes in another language (e.g. Roman Urdu) |

## Endpoints (`/api`, login required)
`auth/login`, `status`, `settings` (GET/PUT), `settings/test/whatsapp`, `settings/test/ai`,
`restaurants` (GET/POST), `restaurants/{rid}` (GET/PUT), `…/menu|deals|faqs|zones` (CRUD),
`…/overview`, `…/orders` (+ `/{id}/status`), `…/bookings` (+ `/{id}/status`), `…/customers` (+ `/pause`, `/resume`, `/messages` GET/POST),
`…/conversations`, `…/simulator`.

## Checklist
- [x] AppSettings migration (`DashboardSettings`)
- [x] Admin API + 6 tests (login, hidden secrets, settings applied immediately, simulator AI/hello switch, simulator order → orders page, staff reply + pause, menu CRUD + tenant isolation). 50 tests passing in total
- [x] Angular pages: Login, Overview (setup checklist + webhook URL), Orders, Chats, Bookings, Customers, Menu/Deals/FAQs/Zones, Restaurant info (+ new restaurant), Settings (+ connection tests), Simulator
- [x] Every page and action checked with Playwright on local Postgres (desktop + mobile, no console errors)
- [x] Dockerfile: a Node stage builds the dashboard
- [x] English by default (UI, backend messages, docs and code comments)
- [ ] Build the Docker image (there was no Docker daemon in that environment)
- [ ] Test from the dashboard with a real Claude key and Meta number
- [ ] After Phase 4: customer labels, no-show button, blacklist on the dashboard
- [ ] Separate login per restaurant owner (multi-tenant SaaS); currently one admin
