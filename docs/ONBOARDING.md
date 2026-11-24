# ChatServe: Going live and onboarding a restaurant

_A checklist for running ChatServe for real shops. Written 24 Nov 2026._

---

## Part 1: Go live first (one time, for you as the operator)

ChatServe must not run on your PC with a test number when real shops use it. Do these once:

| # | What | Why |
|---|---|---|
| 1 | **Deploy online** (Render + Neon is prepared: branch `render-deploy`, `render.yaml`; Render needs a card, paid plan ≈ $7/month) | It must run 24/7, not only while your PC is on |
| 2 | **Meta business verification** of the ChatServe business portfolio (business registration / NTN or a utility bill, website) | Unverified portfolios can't add real phone numbers ("Phone Numbers Count Exceeded Limit Per Business") |
| 3 | **Permanent Meta token** (Business settings → System users → Generate token, expiry "Never", permissions `whatsapp_business_messaging`, `whatsapp_business_management`) | The "Try it out" token expires and replies stop (error 190) |
| 4 | **Payment method** in WhatsApp Manager | Meta charges for some messages (e.g. order updates sent after the 24-hour window, marketing). Replying to customers who message first is mostly free |
| 5 | **Anthropic credits** (console.anthropic.com → Billing) | Every AI reply costs a small amount |
| 6 | **Security:** strong admin password, Meta **App secret** in Settings, **signature check ON** | Stops fake webhook calls and dashboard break-ins |

---

## Part 2: Who can use it

- **Customers:** anyone. Once a shop's real number is live, any WhatsApp user can message it and the bot replies. (The "To / allowed list" only applies to Meta's test number.)
- **Shops:** you onboard each one yourself (you are the operator). Self sign-up by shops needs Meta Tech Provider status and Embedded Signup — a later step.

---

## Part 3: Onboarding a shop (example: a BBQ shop)

### What you take from the shop
1. **A phone number for the bot.** Best: a new SIM that has **never been on WhatsApp**. If they insist on their current number, its WhatsApp account must be deleted first (all its chats are lost).
2. **The SMS code** at verification time (they read it to you, or the SIM is with you for that moment).
3. **Business info:** shop name (becomes the WhatsApp display name), address, opening hours, minimum order, payment methods (COD, JazzCash, Easypaisa…).
4. **Full menu with prices**, deals, **delivery areas with charges**.
5. **FAQs:** halal, parking, family hall, spice level, etc.
6. **Kitchen / manager WhatsApp number** for confirmed-order tickets.
7. Optional: logo for the WhatsApp profile.
8. **Your fee** (Part 5).

### What you give the shop
1. **Their WhatsApp order number** with the bot: answers 24/7, takes orders and table bookings.
2. **A dashboard login** (URL + username + password) that shows only their shop.
3. **A 10-minute training:**
   - **Orders:** move new orders through Preparing → Out for delivery → Completed.
   - **Chats:** take over a conversation (pause the bot, reply yourself), then turn the bot back on.
   - **Menu, deals, FAQs:** switch a dish off when it runs out, update prices.
   - **Restaurant info:** bot ON/OFF.
4. **One rule for the kitchen:** at the start of every shift, send "start" from the kitchen number to the shop's WhatsApp number. Meta only delivers order tickets to a number that messaged the shop in the last 24 hours.

---

## Part 4: Where the shop's number goes (3 places)

1. **Meta — WhatsApp Manager** (your ChatServe portfolio)
   `business.facebook.com` → WhatsApp Manager → **Phone numbers** → **Add phone number** → display name (e.g. "XYZ BBQ") → category Restaurant → `+92 3xx xxxxxxx` → SMS code → done.
   Copy the new **Phone number ID** (a long number, not the phone number).
2. **ChatServe — Restaurant info** (log in as `admin`)
   **New restaurant** → name + **Phone number ID** → Create. Then fill in info, menu, deals, FAQs, delivery areas, kitchen number. Test it in **Bot test (simulator)**.
3. **ChatServe — Users**
   **New owner login** → choose the restaurant → username + password → give these to the shop owner privately.

Nothing else changes: the webhook URL, token and AI key are shared by all shops. (The WhatsApp Business Account must be subscribed to the ChatServe app once — already done for account `1812438219994148`.)

---

## Part 5: What to charge (example — set your own prices)

The plan is a **one-time setup fee + a monthly fee**, with Meta and AI costs included in the monthly fee.

**Your costs per shop (rough):**
- **AI:** usually a few rupees per conversation; a busy shop maybe Rs 1,000–3,000 a month.
- **Meta:** replies to customers who message first are mostly free; order updates after 24 hours and marketing messages cost money.
- **Hosting:** ≈ $7–10 a month shared across all shops.
- **Your time:** setup and support.

**Example package:**
- **Setup (one time):** Rs 15,000–25,000 — number, menu entry, training.
- **Monthly:** Rs 5,000–10,000 — bot, dashboard, AI and Meta costs, support.
- **First 1–2 shops:** a discount in exchange for a review or case study.

**Selling points:** no messages missed during rush hours, orders even at 2 AM, no 25–30% Foodpanda-style commission, fewer fake orders (an order only goes through after the customer replies "confirm").

---

## Good to know
- **The shop's number lives in your Meta account.** If a shop leaves, the number can be moved out of your account.
- **New numbers start with a sending limit** for business-started messages; customers can always message in.
- **The display name** needs Meta's approval (can take a few hours to a few days).
- **Each shop needs its own number.** One number can't serve two shops.
