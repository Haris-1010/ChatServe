# ChatServe: Going live and onboarding a restaurant

_A checklist for running ChatServe for real restaurants. Updated 25 Nov 2026._

Dashboard: `https://chatserve.up.railway.app` · Code: GitHub `main` (Railway runs `main`)

---

## Part 1: One-time setup (before the first real client)

Do these once, as the operator (admin).

| # | What | Why |
|---|---|---|
| 1 | **NTN with the business name "ChatServe"** (Part 6) | Meta needs a document with the business name |
| 2 | **Meta business verification** of the ChatServe business portfolio (developers.facebook.com → ChatServe app → Use cases → Customize → Step 3) with the NTN certificate and a website | Unverified portfolios can't add real phone numbers (or only very few) |
| 3 | **Permanent Meta token** (business.facebook.com → Users → System users → `chatserve-bot` → Generate token, expiry "Never", `whatsapp_business_messaging` + `whatsapp_business_management`) | Done 25 Nov 2026. The "Try it out" token expires and replies stop (error 190) |
| 4 | **Payment method** in WhatsApp Manager → Billing | Meta charges for business-started messages (templates). Replies to customers who message first are mostly free |
| 5 | **Anthropic credits** (console.anthropic.com → Billing; turn on auto-reload if your account offers it) | Every AI reply uses credits. When they run out, customers get "we're having trouble, our team will reply" |
| 6 | **OpenAI API key** in Settings → Voice notes | Voice notes are transcribed (Whisper) and answered. Without it customers get "please type" |
| 7 | **Your WhatsApp number** in Settings → AI cost | You get a WhatsApp alert when a client reaches 80% and 100% of its monthly AI replies. Message the ChatServe number once a day so alerts can reach you (24-hour window) |
| 8 | **Security:** strong admin password, Meta **App secret** in Settings, **Webhook signature check ON** | Stops fake webhook calls and dashboard break-ins |

Already done: Railway deploy, webhook `https://chatserve.up.railway.app/webhook` (field `messages` subscribed), Phone number ID / token / verify token in Settings.

---

## Part 2: Who can use it

- **Customers:** anyone, once a restaurant's real number is live. (Meta's test number only replies to the numbers in its "To" list, max 5. That list is only for testing.)
- **Restaurants:** you onboard each one yourself. Self sign-up needs Meta Tech Provider status and Embedded Signup (a later step).

---

## Part 3: New client in 7 steps (about 1 hour + Meta's display-name approval)

### Step 1: Collect from the client
- [ ] **A phone number for the bot.** Best: a new SIM that has **never been on WhatsApp**. If they want their current number, its WhatsApp account must be deleted first (its chats are lost and it can't be used in the WhatsApp app any more).
- [ ] The **SMS code** at registration time (they read it to you, or the SIM is with you).
- [ ] Restaurant **name** (WhatsApp display name), **logo**, **address**, **timings**
- [ ] **Full menu with prices**, deals
- [ ] **Delivery areas with charges**, minimum order
- [ ] **Payment methods** (COD, JazzCash, Easypaisa…)
- [ ] **FAQs** (halal, family hall, parking, spice level…)
- [ ] **Staff WhatsApp numbers** (kitchen, manager, rider): they get order tickets and run orders from WhatsApp
- [ ] Optional: **old WhatsApp chats** with customers (Export chat → Without media, or screenshots) for Bot training
- [ ] Their **package** (AI replies per month, e.g. 3,000) and your fee (Part 5)

### Step 2: Add the number in Meta (about 10 minutes)
1. developers.facebook.com → ChatServe app → **Use cases → Customize → Step 2. Production setup**
2. **Register your WhatsApp phone number → Add phone number**
3. WhatsApp Business account: **first time "Create new WhatsApp Business account"** (not the "Test" one); later clients: the same account
4. Display name = restaurant name, category **Restaurant**, number `+92 3xx xxxxxxx`, SMS code
5. Copy the **Phone number ID** (a long number, not the phone number)
6. Profile (WhatsApp Manager → Phone numbers → ⚙️ → Profile): logo, description, address

The display name needs Meta's approval (a few hours to a few days). **The first time a new WhatsApp Business account is created, check that it is subscribed to the ChatServe app (webhooks)**, otherwise messages won't reach ChatServe.

### Step 3: Create the client in ChatServe (about 2 minutes)
1. Log in as `admin` → **All clients → + New client**: business name, restaurant name, **Phone number ID**, login username + password (🎲)
2. A new branch of an existing client: **+ Add restaurant** on its card
3. **AI usage** → the client's card → **AI replies per month (package)** → e.g. 3000 → Save limit

### Step 4: Fill in the restaurant (about 20–30 minutes)
Open the restaurant:
1. **Restaurant info:** address, timings, minimum order, payment methods, **staff numbers** (comma-separated, with 92: `923001234567, 923331234567`), bot tone (e.g. "Friendly, short. Roman Urdu if the customer writes Roman Urdu"), **Bot ON**
2. **Menu & FAQs:** dishes, deals, delivery areas and charges, FAQs
3. **Bot training:** upload the old chats (.txt export or screenshots), check the text, remove anything private, Save. 5–10 good chats are enough; phone numbers are hidden automatically.

### Step 5: Test it yourself (about 10 minutes)
1. **Test the bot:** ask for the menu, place an order, try an area you don't deliver to, try an address with two places ("Paragon City, Johar Town")
2. From your own phone, message the **restaurant's real number**: order → reply "haan" → order confirmed
3. Check the **ticket reaches every staff number** with its buttons. From a staff phone tap **👨‍🍳 Prep**, **🛵 Rider gaya**, **✅ Delivered**: the customer gets a message at each step, and after `done` is asked "Kya aap ko order mil gaya?"
4. Send a voice note (if the OpenAI key is set)

Tip: use **different phones** for "customer" and "staff". If one number is both, staff tickets show up in the customer chat.

### Step 6: Hand over to the client (10-minute training)
Give the login (URL + username + password) **privately**. Show them:
- **Orders:** move orders New → Preparing → Out for delivery → Completed
- **Chats:** take over a conversation (bot pauses for that customer), reply yourself, turn the bot back on
- **Menu & FAQs:** switch a dish off when it runs out, update prices
- **Bot training** and **AI usage** (replies used / left this month)

**Staff rules (important):**
- Every staff number sends **`start`** to the restaurant's number **once a day** (Meta only delivers tickets to numbers that messaged in the last 24 hours)
- Every new order arrives with buttons: tap **👨‍🍳 Prep** (being prepared), **🛵 Rider gaya** (rider left), **✅ Delivered / Ready** (delivered / ready for pickup), **❌ Cancel**. After each tap the next buttons come. Typing still works: `prep 12`, `out 12`, `done 12`, `cancel 12`
- Table booking requests arrive with **✅ Confirm booking / ❌ Cancel booking** buttons; the customer is told automatically
- **Always send `done` after delivery**: that's when the customer is asked whether they received the order
- **`orders`** lists all active orders, **`help`** shows the commands

### Step 7: Go live and watch the first week
- The client puts the number on Facebook, Instagram, Google, the menu card: "Order on WhatsApp"
- **First 7 days:** read **Chats** daily. Where the bot answered badly, add an FAQ, fix the menu, or add a training example
- End of the week: check **AI usage** and adjust the package if needed

---

## Part 4: Where things live

| Thing | Where |
|---|---|
| Restaurant's WhatsApp number | Meta → WhatsApp Manager (your ChatServe portfolio). If a client leaves, the number can be moved out |
| Phone number ID | ChatServe → Restaurant info (only admin can change it) |
| Webhook URL, Meta token, App secret, AI key, OpenAI key, prices | ChatServe → Settings (admin only, shared by all restaurants) |
| Staff numbers, menu, FAQs, training | Per restaurant, in ChatServe |
| AI reply limit | Per client, ChatServe → AI usage (admin sets it, client only sees it) |

**Orders never mix between restaurants:** each restaurant has its own WhatsApp number; tickets go only to that restaurant's staff numbers; a staff command only changes orders of the restaurant whose number it was sent to; each client's login sees only its own restaurants. Order numbers (#12…) are shared across all restaurants, so a restaurant's numbers can skip.

---

## Part 5: What to charge (example, set your own prices)

**Your costs per restaurant (rough):**
- **AI:** about **Rs 0.10 per AI reply** with `claude-haiku-5-5` (more with a big menu or long chats). 3,000 replies ≈ Rs 300. See AI usage for real numbers.
- **Voice notes:** OpenAI Whisper, charged per minute of audio (see your OpenAI dashboard; not in AI usage)
- **Meta:** replies to customers who message first are mostly free; template messages (after 24 hours, marketing) cost money
- **Hosting:** ≈ $7–10 a month shared across all restaurants
- **Your time:** setup and support

**Example packages:**

| Package | Setup (one time) | Monthly | AI replies / month |
|---|---|---|---|
| Basic | Rs 15,000 | Rs 5,000 | 3,000 |
| Standard | Rs 20,000 | Rs 8,000 | 8,000 |

First 1–2 restaurants: a discount in exchange for a review or case study.

**Selling points:** no message missed in rush hours, orders at 2 AM, no 25–30% Foodpanda-style commission, fewer fake orders (the order goes to the kitchen only after the customer says yes), staff run orders from WhatsApp, the bot learns the restaurant's own way of talking.

**The limit is a warning only:** at 80% you get a WhatsApp alert and both dashboards show a banner; the bot never stops. Charge extra or move them to a bigger package.

---

## Part 6: Getting an NTN with the business name (for Meta verification)

An individual's NTN is free and done online on FBR's **IRIS** portal. FBR's screens change from time to time; if you get stuck, a tax consultant can do it in a day for a small fee.

**You need:** CNIC, a mobile SIM **registered in your own name**, an email address, and the business address.

1. Open **iris.fbr.gov.pk** → **Registration for unregistered person**
2. Enter CNIC, name, mobile number and email → enter the codes sent by SMS and email → set your IRIS password
3. Log in → the **Registration form (Form 181)** opens. Fill in your personal details and address
4. In the **Business** section add a business:
   - Business name: **ChatServe**
   - Nature of business: software / IT services (pick the closest activity)
   - Business address (can be your home address; it must match what you give Meta)
5. Submit. For an individual, the NTN is usually the CNIC number
6. Download the **Registration Certificate** from IRIS and check that it shows **ChatServe** and the address
7. Already registered without a business name? Log in → modify registration (Form 181) → add the business → submit

**Then in Meta:** business verification → business type **Sole Proprietorship** → upload the NTN certificate. The business name and address in the ChatServe portfolio must match the certificate exactly.

**Also needed for Meta:** a website with the name ChatServe and contact details (a simple About / Contact / Privacy page is enough; `PRIVACY.md` can become the privacy page).

**After registering:** you are expected to file an annual income tax return. Filing keeps you on FBR's Active Taxpayer List (lower withholding tax); not filing can bring notices and penalties. Ask a tax consultant if unsure.

---

## Good to know
- **New numbers start with a sending limit** for business-started messages; customers can always message in.
- **The display name** needs Meta's approval (a few hours to a few days).
- **Each restaurant needs its own number.** One number can't serve two restaurants.
- **Meta's test number** (+1 555…) only reaches numbers in its "To" list (max 5). Use it only for testing.
- **Deploys:** Railway runs `main`. Don't press "Redeploy" on an old deployment in Railway: it puts old code back live.
