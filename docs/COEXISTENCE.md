# WhatsApp coexistence (app + bot on the same number)

_Added 26 Nov 2026, branch `whatsapp-coexistence`._

ChatServe supports two ways to connect a restaurant's number. Both work side by side, restaurant by restaurant.

| | **API only (classic)** | **Coexistence** |
|---|---|---|
| Number in the WhatsApp Business app | No (must be deleted from the app first) | **Yes, keeps working** |
| How it is connected | Added by hand in Meta (Production setup) | "Connect WhatsApp Business app number" button (Embedded Signup) |
| Token | App-wide token in Settings | The client's own business token (saved per restaurant) |
| Owner replies from the phone | Not possible | Shown in Chats with 📱; the bot pauses for that customer |
| Old chats | None | Up to 6 months imported from the phone (if the owner allows it) |

Existing restaurants stay on **API only**; nothing changes for them.

## What the code does

- **Webhook fields:** `messages` (as before), plus `smb_message_echoes`, `history`, `smb_app_state_sync`.
- **Owner reply from the phone** (`smb_message_echoes`): saved as a Staff message `📱 …`. If "Pause the bot…" is ON (default), the bot pauses for that customer for *Handover pause (hours)* (Settings > Orders, default 12). Echoes to staff numbers are ignored. Nothing is sent.
- **History** (`history`): old chats are imported as Customer/Staff messages (no duplicates, staff numbers skipped). They do not pause the bot; the bot uses them as memory.
- **Contacts** (`smb_app_state_sync`): names the owner saved are stored (`PhoneContacts`) and used as customer names when a customer has none.
- **Per-number token:** a restaurant connected with Embedded Signup sends with its own token; all others use the app-wide token.
- **Admin page:** Restaurant info > **WhatsApp connection** (admin only): mode, test this number, ask for history again, pause switch, Embedded Signup buttons, manual settings.
- API: `GET/PUT /api/restaurants/{rid}/whatsapp`, `POST …/whatsapp/connect`, `…/whatsapp/sync`, `…/whatsapp/test`, `GET /api/whatsapp/signup-config`.

## One-time Meta setup (needs business verification first)

1. **Become a Tech Provider:** developers.facebook.com > ChatServe app > *Become a Partner > Become Tech Provider*. Finish business verification, app review for **advanced access** to `whatsapp_business_management` and `whatsapp_business_messaging`, and access verification.
2. **Embedded Signup configuration:** app > *Facebook Login for Business > Configurations > Create* > choose the WhatsApp Embedded Signup template. Copy the **Configuration ID**.
3. **Allowed domains:** in Facebook Login for Business settings, add `https://chatserve.up.railway.app` (Allowed domains for the JavaScript SDK, and Valid OAuth redirect URIs). The button only works over HTTPS from that domain.
4. **Webhook fields:** WhatsApp > Configuration > Webhook fields: subscribe to `messages`, **`smb_message_echoes`**, **`history`**, **`smb_app_state_sync`**.
5. **ChatServe Settings > WhatsApp:** fill **App ID**, **Embedded Signup configuration ID**, and the **App secret** (already set).

## Connecting a client (coexistence)

1. The client has WhatsApp Business app **2.24.17 or newer** on the number and has used it for a while.
2. ChatServe > open the restaurant > **Restaurant info > WhatsApp connection > 📱 Connect WhatsApp Business app number**.
3. In the Facebook popup the client logs in, picks/creates the business portfolio, enters the number, and **scans the QR code** shown, from the WhatsApp Business app. They should choose to **share chat history**.
4. The card shows each step (token, webhooks, contacts, history). Click **Test this number**.
5. Within 24 hours, use **Ask for phone history again** if history was not requested. Meta only allows it within 24 hours of onboarding.

**Tell the client:** open the WhatsApp Business app at least **every 14 days**, or the connection can drop. In normal chats, disappearing messages, view-once and live location are turned off by Meta. Groups keep working in the app but are not synced.

## Check on the first real client

The webhook formats were written from Meta's documentation; they have not been tested against a live coexistence number yet. On the first connection, watch the Railway logs for `Phone sync`, `Imported … old messages` and `Owner replied from the phone`, and check Chats. If something is off, the raw webhook body is the thing to look at.
