# WhatsApp AI Agent: Project Context (ChatServe)

> **Naye session mein Claude ke liye:** Ye file Talha (full stack .NET developer, Lahore) ke project ka poora context hai. Is se aage kaam shuru karo. Talha se **Roman Urdu** mein baat karo. Pichli baatein dobara mat samjhao, seedha **"Current status / Agla qadam"** wale section se kaam start karo.

---

## 1. Project kya hai
Ek **WhatsApp AI auto-reply agent**, SaaS product ke taur par, jo restaurants aur e-commerce brands ko becha jayega.
- Customer restaurant ke WhatsApp number par message karta hai.
- AI bot restaurant ke number se khud jawab deta hai: menu, prices, delivery, order lena, order status, table booking.
- Restaurant owner ek dashboard se menu aur FAQs update karta hai, aur chats aur orders dekhta hai.
- Pehla target: Lahore ke medium size restaurants (Johar Town, DHA, Gulberg) jo WhatsApp par orders lete hain.

## 2. Faisle jo ho chuke hain
| Cheez | Faisla |
|---|---|
| Approach | **Option B: custom AI agent** (Meta ka Business Agent nahi, kyunke us mein orders, POS aur courier ka custom logic nahi ho sakta) |
| WhatsApp | **Meta WhatsApp Cloud API** (Graph API, `graph.facebook.com/vXX.X/{phone-number-id}/messages`), seedha **HttpClient** se, koi SDK nahi |
| Backend | **ASP.NET Core Web API**, Docker-ready |
| AI | **Claude API**, model **Claude Haiku 5.5** (sasta aur tez). Key console.anthropic.com se. Talha ka Claude Pro plan API mein shamil nahi, API ki billing alag hai |
| Database | **PostgreSQL** + EF Core (`UseNpgsql`), kyunke Railway/Render par SQL Server nahi milta |
| Dashboard | **Angular** |
| Hosting | Backend: **Railway ya Render (paid plan, Docker)**. Free plan so jata hai, us se webhook late hota hai. Dashboard: **Vercel (free)** |
| Architecture | **Multi-tenant SaaS**: ek app, kai restaurants. Webhook payload ke `phone_number_id` se restaurant pehchana jata hai, har table mein `RestaurantId` |
| Project naam | Tentative **"ChatServe"** (Talha ne final confirm nahi kiya) |

## 3. Message ka flow
```
Customer (WhatsApp) → Meta Cloud API → webhook (ASP.NET Core)
  → restaurant ka data (DB) + chat history → Claude AI (tool calling)
  → reply → Graph API → customer ko restaurant ke number se jawab
```
- **GET /webhook:** Meta verification (`hub.verify_token`, `hub.challenge` wapas bhejna)
- **POST /webhook:** message receive karna, foran 200 OK dena, phir background queue mein process karna
- Har phone number ki aakhri 15 se 20 messages ki memory DB mein

## 4. AI agent ki "training" (RAG + prompt + tools, fine-tuning nahi)
- **Data:** menu table (dish, category, price, available), deals, settings (timings, delivery areas aur charges, minimum order, payment methods), FAQs
- **System prompt:** restaurant ka naam aur lehja, sirf diye gaye data se jawab, Roman Urdu/English, order se pehle naam, address aur total confirm karwana
- **Tools (function calling):** `GetMenu`, `CheckDeliveryArea`, `PlaceOrder`, `GetOrderStatus`, `BookTable`, `HandoverToHuman`
- **Feedback loop:** chats log hon, owner ghalat jawab dekh kar dashboard se data ya FAQs update kare

## 5. Customer tagging aur fake order protection
- Labels: **Ordered / Interested / Browsing / Fake-Risky**. Har chat ke baad AI se classification ka chhota call
- `Customers` table: phone, naam, label, total orders, no-show count, last chat
- Order confirmation zaroori ("confirm" ya "1"). Bina confirm ke order kitchen tak nahi jayega
- No-show button (staff ke liye). 1 no-show par extra confirmation, 2 par advance payment (JazzCash/Easypaisa), zyada par blacklist
- Interested customers ko baad mein marketing template (opt-in ke saath)
- Dashboard par filters + Excel export

## 6. Ahem rules aur kharche (Pakistan, Nov 2026)
- **24-hour window:** customer ke aakhri message ke 24 ghante baad sirf approved templates ja sakte hain
- **Meta charges:** Pakistan mein service, utility aur authentication message takreeban **$0.015 fi message**. Har number ko **1,000 service messages free** har mahine (1 Oct 2026 se service replies bhi billable). Marketing is se mehenga hai. Ye kharcha restaurant ke package mein shamil karna hai
- Bot ko **kam messages mein poora jawab** dene wala banana hai (menu image/PDF mein, ek message mein dish + price + delivery charge)
- Doosre kharche: hosting (Railway/Render paid), Claude API usage, domain (optional), **international payment card** (Meta, Anthropic aur hosting ke liye)
- API number WhatsApp app par active nahi hona chahiye
- Live karne ke liye **System User ka permanent token** chahiye (temporary token 24 ghante mein expire)

## 7. Talha ko khud kya karna hai
- Meta Business Manager + Developer App banana, WhatsApp product add karna, test number lena, baad mein business verification
- console.anthropic.com par account, credits aur API key
- Railway ya Render par account, GitHub repo
- International payment card
- Testing ke liye ngrok ya VS Dev Tunnels
- Restaurants ko demo dikha kar bechna (test number par nakli restaurant "Talha's Karahi" ka demo)

## 8. Banane ke phases
| Phase | Kaam |
|---|---|
| **1** | Webhook (GET verify + POST receive) + simple "hello" auto-reply. Local testing ngrok aur Meta test number ke saath |
| **2** | Claude AI + menu se jawab (system prompt, chat memory) |
| **3** | Order flow: tools, confirmation, kitchen tak order |
| **4** | Customer labels, no-shows, blacklist |
| **5** | Angular dashboard: menu, live orders, chats, customers, Excel export, bot ON/OFF, human takeover |
| **6** | Deployment: Docker → Railway/Render, PostgreSQL, Angular → Vercel |
| **7** | Multi-tenant onboarding + templates (order ready, rider nikal gaya). Baad mein Meta Tech Provider + Embedded Signup |

## 9. Current status / Agla qadam
- ✅ Planning aur architecture mukammal
- ✅ **Phase 1 mukammal** (24 Nov 2026): webhook GET/POST, signature check, queue, duplicate protection, Graph API client, Dockerfile
- ✅ **Phase 2 mukammal** (code + tests), tafseel `docs/PHASE2_PROGRESS.md`. Asli Claude key se test baqi
- ✅ **Phase 3 mukammal** (24 Nov 2026): order tools, server-side totals, "confirm" par kitchen ko WhatsApp ticket, order status, table booking, human handover. Tafseel `docs/PHASE3_PROGRESS.md`
- ✅ **Phase 5 (dashboard) ka bara hissa** (24 Nov 2026): Angular 22 dashboard (`dashboard/`) + admin API (`src/ChatServe.Api/Dashboard/`). Settings UI se (DB mein, foran lagu), menu/deals/FAQs/zones, orders, chats + staff reply + bot pause, bookings, customers CSV, bot simulator. Tafseel `docs/PHASE5_PROGRESS.md`
- ⏭️ **Agla: Phase 4** (customer labels, no-shows, blacklist), phir dashboard par labels/filters
- ⏳ **Talha ka kaam:** Meta app setup (README), Anthropic API key, `D:\Development\New folder` ko Claude desktop app (Settings > Claude Code > Remote Control) mein share karna taake code wahan copy ho
- GitHub: `Haris-1010/ChatServe` (public). Dashboard wala kaam branch/zip se wahan jana hai

## 10. Bechne ka plan (mukhtasar)
- Demo dikhayen: owner khud apne phone se test number par message kare
- Pitch: rush ke waqt messages miss nahi honge, raat 12 baje ke orders bhi aayenge, Foodpanda jaisa 25 se 30% commission nahi, fake orders kam honge
- Package: setup fee + mahana fee (Meta aur AI kharche shamil). Pehle 1 se 2 clients ko discount, badle mein review ya case study
