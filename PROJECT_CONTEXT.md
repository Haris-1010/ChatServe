# WhatsApp AI Agent: Project Context (ChatServe)

> **For Claude in a new session:** Also read `docs/SESSION_HANDOVER.md` (latest session). This file is the full context of the project by Talha (full stack .NET developer, Lahore). Continue the work from here. Talk with Talha in **Roman Urdu**. Don't re-explain earlier discussions; start directly from the **"Current status / Next step"** section.

---

## 1. What the project is
A **WhatsApp AI auto-reply agent**, sold as a SaaS product to restaurants and e-commerce brands.
- A customer messages the restaurant's WhatsApp number.
- The AI bot replies from the restaurant's number: menu, prices, delivery, taking orders, order status, table booking.
- The restaurant owner updates the menu and FAQs from a dashboard, and views chats and orders.
- First target: medium-sized restaurants in Lahore (Johar Town, DHA, Gulberg) that take orders on WhatsApp.

## 2. Decisions already made
| Item | Decision |
|---|---|
| Approach | **Option B: custom AI agent** (not Meta's Business Agent, because it can't do custom logic for orders, POS and couriers) |
| WhatsApp | **Meta WhatsApp Cloud API** (Graph API, `graph.facebook.com/vXX.X/{phone-number-id}/messages`), called directly with **HttpClient**, no SDK |
| Backend | **ASP.NET Core Web API**, Docker-ready |
| AI | **Claude API**, model **Claude Haiku 5.5** (cheap and fast). Key from console.anthropic.com. Talha's Claude Pro plan doesn't include the API; API billing is separate |
| Database | **PostgreSQL** + EF Core (`UseNpgsql`), because Railway/Render don't offer SQL Server |
| Dashboard | **Angular** |
| Hosting | **Railway** (Docker, API + dashboard on one URL) + Railway PostgreSQL. See `docs/DEPLOY_RAILWAY.md` |
| Architecture | **Multi-tenant SaaS**: one app, many restaurants. The restaurant is identified by the `phone_number_id` in the webhook payload; every table has a `RestaurantId` |
| Language | **English by default** for the whole product (dashboard, messages, docs, code). The bot replies in the customer's language if they write in something other than English (e.g. Roman Urdu) |
| Project name | Tentatively **"ChatServe"** (Talha hasn't confirmed it as final) |

## 3. Message flow
```
Customer (WhatsApp) → Meta Cloud API → webhook (ASP.NET Core)
  → restaurant data (DB) + chat history → Claude AI (tool calling)
  → reply → Graph API → reply to the customer from the restaurant's number
```
- **GET /webhook:** Meta verification (return `hub.challenge` when `hub.verify_token` matches)
- **POST /webhook:** receive the message, return 200 OK immediately, then process it in a background queue
- Memory of the last 15 to 20 messages per phone number in the DB

## 4. "Training" the AI agent (RAG + prompt + tools, no fine-tuning)
- **Data:** menu table (dish, category, price, available), deals, settings (timings, delivery areas and charges, minimum order, payment methods), FAQs
- **System prompt:** the restaurant's name and tone, answer only from the given data, English by default (or the customer's language), confirm name, address and total before an order
- **Tools (function calling):** `GetMenu`, `CheckDeliveryArea`, `PlaceOrder`, `GetOrderStatus`, `BookTable`, `HandoverToHuman`
- **Feedback loop:** chats are logged; the owner reviews wrong answers and updates the data or FAQs from the dashboard

## 5. Customer tagging and fake order protection
- Labels: **Ordered / Interested / Browsing / Fake-Risky**. A small AI classification call after each chat
- `Customers` table: phone, name, label, total orders, no-show count, last chat
- Order confirmation is required ("confirm" or "1"). Without confirmation, no order reaches the kitchen
- No-show button (for staff). 1 no-show → extra confirmation, 2 → advance payment (JazzCash/Easypaisa), more → blacklist
- Later, marketing templates for interested customers (with opt-in)
- Filters + Excel export on the dashboard

## 6. Important rules and costs (Pakistan, Nov 2026)
- **24-hour window:** 24 hours after the customer's last message, only approved templates can be sent
- **Meta charges:** in Pakistan, service, utility and authentication messages cost roughly **$0.015 per message**. Each number gets **1,000 free service messages** per month (from 1 Oct 2026, service replies are billable too). Marketing costs more. These costs must be included in the restaurant's package
- The bot should give **complete answers in few messages** (menu as an image/PDF, dish + price + delivery charge in one message)
- Other costs: hosting (Railway/Render paid), Claude API usage, domain (optional), an **international payment card** (for Meta, Anthropic and hosting)
- The API number must not be active in the WhatsApp app
- Going live needs a **permanent System User token** (the temporary token expires in 24 hours)

## 7. What Talha needs to do
- Create a Meta Business Manager + Developer App, add the WhatsApp product, get a test number, and later do business verification
- An account, credits and an API key on console.anthropic.com
- An account on Railway or Render, a GitHub repo
- An international payment card
- ngrok or VS Dev Tunnels for testing
- Sell to restaurants by showing a demo (a fake restaurant "Talha's Karahi" on the test number)

## 8. Build phases
| Phase | Work |
|---|---|
| **1** | Webhook (GET verify + POST receive) + simple "hello" auto-reply. Local testing with ngrok and the Meta test number |
| **2** | Claude AI + answers from the menu (system prompt, chat memory) |
| **3** | Order flow: tools, confirmation, sending orders to the kitchen |
| **4** | Customer labels, no-shows, blacklist |
| **5** | Angular dashboard: menu, live orders, chats, customers, Excel export, bot ON/OFF, human takeover |
| **6** | Deployment: Docker → Railway/Render, PostgreSQL, Angular → Vercel |
| **7** | Multi-tenant onboarding + templates (order ready, out for delivery). Later, Meta Tech Provider + Embedded Signup |

## 9. Current status / Next step
- ✅ Planning and architecture complete
- ✅ **Phase 1 complete** (24 Nov 2026): webhook GET/POST, signature check, queue, duplicate protection, Graph API client, Dockerfile
- ✅ **Phase 2 complete** (code + tests), details in `docs/PHASE2_PROGRESS.md`. Testing with a real Claude key is still pending
- ✅ **Phase 3 complete** (24 Nov 2026): order tools, server-side totals, WhatsApp ticket to the kitchen on "confirm", order status, table booking, human handover. Details in `docs/PHASE3_PROGRESS.md`
- ✅ **Most of Phase 5 (dashboard)** (24 Nov 2026): Angular 22 dashboard (`dashboard/`) + admin API (`src/ChatServe.Api/Dashboard/`). Settings from the UI (in the DB, applied immediately), menu/deals/FAQs/zones, orders, chats + staff reply + bot pause, bookings, customers CSV, bot simulator. Details in `docs/PHASE5_PROGRESS.md`
- ✅ **English by default** (24 Nov 2026): dashboard, settings, customer/kitchen messages, AI default reply language, demo data, docs and code comments
- ✅ **Railway + multi-client portal** (25 Nov 2026): `railway.json` + `docs/DEPLOY_RAILWAY.md`; clients (organizations) with many restaurants each, client logins see only their own restaurants (server-side 403), admin **All clients** page with today's numbers per client/restaurant, suspend client; login keys stored in the DB so redeploys don't log people out
- ⏭️ **Next: Phase 4** (customer labels, no-shows, blacklist), then labels/filters on the dashboard
- ⏳ **Talha's tasks:** Meta app setup (see README), Anthropic API key, share `D:\Development\New folder` with the Claude desktop app (Settings > Claude Code > Remote Control) so the code can be copied there
- GitHub: `Haris-1010/ChatServe` (public). The dashboard work still has to go there from a branch/zip

## 10. Sales plan (short)
- Show a demo: the owner messages the test number from their own phone
- Pitch: no missed messages during rush hours, orders even at midnight, no 25 to 30% commission like Foodpanda, fewer fake orders
- Package: setup fee + monthly fee (Meta and AI costs included). Discount for the first 1 to 2 clients in exchange for a review or case study
