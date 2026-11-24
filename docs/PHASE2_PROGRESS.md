# Phase 2 Progress: Claude AI + menu + chat memory

> For a new session: read `PROJECT_CONTEXT.md` first, then this file. Don't rebuild anything marked ✅.

## Decisions (Phase 2)
| Item | Decision |
|---|---|
| Claude SDK | Official NuGet `Anthropic` 13.13.0 (`client.Messages.Create`) |
| Model | `claude-haiku-5-5`, `Effort = low` (cheap/fast for chat). Thinking is on by default; not set separately |
| How the menu reaches the AI | All restaurant data is in the system prompt (RAG/tools in Phase 3). `cache_control` on the system prompt (Haiku 5.5 caches above 512 tokens) |
| Time | Pakistan time (UTC+5) is added as a note to the last customer message, not to the system prompt (to keep the cache) |
| Memory | The last `HistoryLimit` (20) messages from the `ChatMessages` table. Consecutive messages with the same role are merged; the first message is the customer's |
| Refusal / API error | A fixed `FallbackReply` ("staff will contact you"). Haiku has no server-side fallback |
| Non-text (image/voice) | A fixed reply without an AI call (saves money) |
| DB | PostgreSQL + EF Core 8 (Npgsql). On startup: `Migrate()` + demo seed "Talha's Karahi" (for the configured `PhoneNumberId`) |
| Connection | `ConnectionStrings:Default` or `DATABASE_URL` (a postgres:// URL also works) |
| Switch | `Bot:UseAi=false` falls back to the Phase 1 hello reply |

## Files (new in Phase 2)
- `Data/Entities.cs`: Restaurant, MenuItem, Deal, Faq, Customer, ChatMessage (token usage is saved too)
- `Data/AppDbContext.cs`, `Data/DemoSeeder.cs`, `Data/Migrations/*` (InitialCreate)
- `Ai/AiOptions.cs`, `Ai/ChatCompletionClient.cs` (IChatCompletionClient + Claude implementation), `Ai/SystemPromptBuilder.cs`
- `Messaging/ClaudeReplyGenerator.cs`: find restaurant → upsert customer → save message → history → Claude → save reply
- `Infrastructure/DatabaseUrl.cs`

## Checklist
- [x] Packages: Anthropic, Npgsql EF Core 8.0.11, EF Design
- [x] Entities + DbContext + migration
- [x] Claude client + system prompt + memory + reply generator
- [x] Program.cs wiring
- [x] Tests: 25 passing (prompt builder, history, reply generator with a fake AI, keyed lock, webhook e2e with an in-memory DB)
- [x] End-to-end run on local Postgres 16: migration + seed + 2 messages, memory passed correctly, `cache_control` + `effort: low` in the request, tokens saved in the DB (a fake server stood in for Claude; there was no API key)
- [x] Per-customer lock (`KeyedLock`): one customer's messages are processed one at a time
- [x] README update (Anthropic key, Postgres setup)
- [ ] **Test with a real Claude key** (once Talha's key is available): replies in the customer's language, correct prices, cache hit (`CacheReadTokens` > 0 from the second message)
- [ ] Copy the code to `D:\Development\New folder` (after the folder is shared)

## Settings Talha needs to set
```
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=chatserve;Username=postgres;Password=..."
```

## Notes for Phase 3 (next step)
- Tools: `GetMenu`, `CheckDeliveryArea`, `PlaceOrder` (after confirm), `GetOrderStatus`, `BookTable`, `HandoverToHuman`
- `Orders`, `OrderItems`, `TableBookings` tables + migration
- Tool loop: Haiku 5.5 doesn't support forced `tool_choice`; use `auto` + `strict: true`
- The "Online order placement is not live yet" line in the system prompt must change in Phase 3
