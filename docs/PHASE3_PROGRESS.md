# Phase 3 Progress: Order flow (tools, confirmation, kitchen)

> Naye session ke liye: pehle `PROJECT_CONTEXT.md`, phir ye file. Jo ✅ hai use dobara mat banao.

## Faisle (Phase 3)
| Cheez | Faisla |
|---|---|
| Totals | **Server** menu ki prices se calculate karta hai (`OrderService`). AI ka hisaab kabhi use nahi hota |
| Confirm | **AI ke baghair**: pending draft ho aur customer "confirm" (ya summary ke foran baad "1") likhe to server khud confirm karta hai. Fixed jawab, AI call nahi (sasta + mehfooz). "cancel" = draft cancel |
| Kitchen tak | Confirm par `Restaurant.StaffWhatsAppNumber` par WhatsApp ticket (`WhatsAppStaffNotifier`). Number na ho to sirf log |
| Tools | `place_order` (draft banao/badlo), `get_order_status`, `book_table`, `handover_to_human`. Sab `strict: true`, `tool_choice` auto (Haiku 5.5 par forced nahi hota) |
| GetMenu / CheckDeliveryArea | Alag tools **nahi** banaye: menu pehle se cached system prompt mein hai, aur delivery area `place_order` ke schema mein enum + server check hai. Extra round-trip = extra kharcha aur der |
| Item codes | Prompt mein `[M12]` (menu) aur `[D2]` (deal). AI tool mein code bhejta hai, naam nahi |
| Delivery areas | Nayi `DeliveryZones` table (naam + charge). `Restaurant.DeliveryAreas` ab sirf notes |
| Draft memory | Tool calls history mein save nahi hote; pending draft aakhri customer message ke saath note ki shakal mein AI ko jata hai (cache nahi toot'ta) |
| Hifazat | Draft bana ho aur AI ke jawab mein sahi total na ho to server summary khud jod deta hai. Ek customer ka ek hi pending draft. Draft 180 min baad expire |
| Handover | `Customer.BotPausedUntil` = ab + 12 ghante; staff ko alert. Is dauran bot chup (message phir bhi save) |
| Order number | `Orders.Id` hi customer ko `#Id` dikhta hai |

## Files (Phase 3 mein nayi/badli)
- `Data/Entities.cs`: DeliveryZone, Order, OrderItem, TableBooking, enums; Restaurant.StaffWhatsAppNumber; Customer.BotPausedUntil
- `Data/Migrations/*_Phase3Orders.cs`
- `Ai/ChatCompletionClient.cs`: manual tool loop (thinking + tool_use echo, saare tool_results ek message mein, `MaxToolRounds`)
- `Ai/SystemPromptBuilder.cs`: item codes, zones, ordering rules
- `Orders/OrderService.cs`, `Orders/RestaurantTools.cs` (definitions + `OrderToolExecutor`), `Orders/OrderFormatter.cs`, `Orders/StaffNotifier.cs`, `Orders/OrderOptions.cs`, `Orders/Money.cs`
- `Messaging/ClaudeReplyGenerator.cs`: confirm/cancel, handover pause, tools wiring, staff alerts
- `Data/DemoSeeder.cs`: zones + staff number; purane (Phase 2) demo restaurant ko bhi upgrade karta hai

## Checklist
- [x] Entities + migration
- [x] Tool loop (SDK `Anthropic` 13.13.0, strict tools)
- [x] Order draft, confirm, cancel, kitchen ticket
- [x] Order status, table booking, human handover
- [x] Tests: 44 pass (order flow, "1" sirf summary ke baad, invalid orders, booking, handover, prompt/tool determinism)
- [x] Local Postgres 16 par end-to-end (nakli Claude + nakli Graph server): Phase 2 DB par migration chali, SDK ne 4 strict tools + effort low + cache_control bheje, thinking/tool_use echo hua, Rs 5,300 server ne calculate kiya, "confirm" par staff number par ticket aur customer ko `#1` confirm
- [ ] **Asli Claude key ke saath test**: Roman Urdu mein order lena, place_order sahi codes ke saath, summary bhejna
- [ ] Staff number ka 24-hour window test (asli Meta number par)
- [ ] Code `D:\Development\New folder` mein copy

## Settings (naye)
```
dotnet user-secrets set "Bot:DemoStaffWhatsAppNumber" "923001234567"
```
`Orders:DraftExpiryMinutes` (180), `Orders:HandoverPauseHours` (12), `Orders:MaxQuantityPerItem` (30), `Orders:MaxBookingDaysAhead` (30), `Anthropic:MaxToolRounds` (5).

## Phase 4 ke liye notes (agla qadam)
- `Customer`: Label (Ordered / Interested / Browsing / Fake-Risky), TotalOrders, NoShowCount, IsBlacklisted
- Confirm ke waqt TotalOrders++ (`ClaudeReplyGenerator` ka confirm wala hissa)
- No-show rules: 1 = extra confirmation, 2 = advance payment, zyada = blacklist (`OrderService.SaveDraftAsync` mein check)
- Classification ka chhota AI call har chat ke baad (Haiku, structured output)
