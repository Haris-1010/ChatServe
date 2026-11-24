# Phase 3 Progress: Order flow (tools, confirmation, kitchen)

> For a new session: read `PROJECT_CONTEXT.md` first, then this file. Don't rebuild anything marked ✅.

## Decisions (Phase 3)
| Item | Decision |
|---|---|
| Totals | The **server** calculates them from menu prices (`OrderService`). The AI's math is never used |
| Confirm | **Without AI**: if there is a pending draft and the customer writes "confirm" (or "1" right after the summary), the server confirms it itself. Fixed reply, no AI call (cheaper + safer). "cancel" = cancel the draft |
| To the kitchen | On confirm, a WhatsApp ticket goes to `Restaurant.StaffWhatsAppNumber` (`WhatsAppStaffNotifier`). Without a number, it is only logged |
| Tools | `place_order` (create/replace draft), `get_order_status`, `book_table`, `handover_to_human`. All `strict: true`, `tool_choice` auto (Haiku 5.5 doesn't support forced) |
| GetMenu / CheckDeliveryArea | **Not** built as separate tools: the menu is already in the cached system prompt, and the delivery area is an enum in the `place_order` schema + a server check. An extra round trip = extra cost and delay |
| Item codes | `[M12]` (menu) and `[D2]` (deal) in the prompt. The AI sends the code in the tool, not the name |
| Delivery areas | New `DeliveryZones` table (name + charge). `Restaurant.DeliveryAreas` is now only notes |
| Draft memory | Tool calls aren't saved in history; the pending draft is sent to the AI as a note with the last customer message (doesn't break the cache) |
| Safety net | If a draft exists and the AI's reply lacks the correct total, the server appends the summary itself. One pending draft per customer. Drafts expire after 180 min |
| Handover | `Customer.BotPausedUntil` = now + 12 hours; staff get an alert. The bot stays quiet meanwhile (messages are still saved) |
| Order number | `Orders.Id` is what the customer sees as `#Id` |

## Files (new/changed in Phase 3)
- `Data/Entities.cs`: DeliveryZone, Order, OrderItem, TableBooking, enums; Restaurant.StaffWhatsAppNumber; Customer.BotPausedUntil
- `Data/Migrations/*_Phase3Orders.cs`
- `Ai/ChatCompletionClient.cs`: manual tool loop (echoes thinking + tool_use, all tool_results in one message, `MaxToolRounds`)
- `Ai/SystemPromptBuilder.cs`: item codes, zones, ordering rules
- `Orders/OrderService.cs`, `Orders/RestaurantTools.cs` (definitions + `OrderToolExecutor`), `Orders/OrderFormatter.cs`, `Orders/StaffNotifier.cs`, `Orders/OrderOptions.cs`, `Orders/Money.cs`
- `Messaging/ClaudeReplyGenerator.cs`: confirm/cancel, handover pause, tools wiring, staff alerts
- `Data/DemoSeeder.cs`: zones + staff number; also upgrades the old (Phase 2) demo restaurant

## Checklist
- [x] Entities + migration
- [x] Tool loop (SDK `Anthropic` 13.13.0, strict tools)
- [x] Order draft, confirm, cancel, kitchen ticket
- [x] Order status, table booking, human handover
- [x] Tests: 44 passing (order flow, "1" only right after the summary, invalid orders, booking, handover, prompt/tool determinism)
- [x] End-to-end on local Postgres 16 (fake Claude + fake Graph server): the migration ran on the Phase 2 DB, the SDK sent 4 strict tools + effort low + cache_control, thinking/tool_use were echoed, the server calculated Rs 5,300, and on "confirm" a ticket went to the staff number and the customer got confirmation `#1`
- [ ] **Test with a real Claude key**: take an order (including in Roman Urdu), place_order with correct codes, send the summary
- [ ] Test the staff number's 24-hour window (on a real Meta number)
- [ ] Copy the code to `D:\Development\New folder`

## Settings (new)
```
dotnet user-secrets set "Bot:DemoStaffWhatsAppNumber" "923001234567"
```
`Orders:DraftExpiryMinutes` (180), `Orders:HandoverPauseHours` (12), `Orders:MaxQuantityPerItem` (30), `Orders:MaxBookingDaysAhead` (30), `Anthropic:MaxToolRounds` (5).

## Notes for Phase 4 (next step)
- `Customer`: Label (Ordered / Interested / Browsing / Fake-Risky), TotalOrders, NoShowCount, IsBlacklisted
- TotalOrders++ on confirm (the confirm part of `ClaudeReplyGenerator`)
- No-show rules: 1 = extra confirmation, 2 = advance payment, more = blacklist (checked in `OrderService.SaveDraftAsync`)
- A small AI classification call after each chat (Haiku, structured output)
