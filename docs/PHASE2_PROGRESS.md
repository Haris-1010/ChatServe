# Phase 2 Progress: Claude AI + menu + chat memory

> Naye session ke liye: pehle `PROJECT_CONTEXT.md` parhein, phir ye file. Jo ✅ hai use dobara mat banao.

## Faisle (Phase 2)
| Cheez | Faisla |
|---|---|
| Claude SDK | Official NuGet `Anthropic` 13.13.0 (`client.Messages.Create`) |
| Model | `claude-haiku-5-5`, `Effort = low` (chat ke liye sasta/tez). Thinking default on hai, alag se set nahi kiya |
| Menu AI tak kaise | Poora restaurant data system prompt mein (RAG/tools Phase 3 mein). System prompt par `cache_control` (Haiku 5.5 par 512 token se upar cache hota hai) |
| Waqt | Pakistan time (UTC+5) aakhri customer message ke saath note ki shakal mein, system prompt mein nahi (cache bachane ke liye) |
| Memory | `ChatMessages` table se aakhri `HistoryLimit` (20) messages. Lagataar same role wale jod diye jaate hain, pehla message customer ka |
| Refusal / API error | Fixed `FallbackReply` ("staff rabta karega"). Haiku par server-side fallback nahi hota |
| Non-text (image/voice) | AI call ke baghair fixed jawab (paisa bachao) |
| DB | PostgreSQL + EF Core 8 (Npgsql). Startup par `Migrate()` + demo seed "Talha's Karahi" (configured `PhoneNumberId` par) |
| Connection | `ConnectionStrings:Default` ya `DATABASE_URL` (postgres:// URL bhi chalta hai) |
| Switch | `Bot:UseAi=false` karne par Phase 1 wala hello reply |

## Files (Phase 2 mein nayi)
- `Data/Entities.cs`: Restaurant, MenuItem, Deal, Faq, Customer, ChatMessage (token usage bhi save)
- `Data/AppDbContext.cs`, `Data/DemoSeeder.cs`, `Data/Migrations/*` (InitialCreate)
- `Ai/AiOptions.cs`, `Ai/ChatCompletionClient.cs` (IChatCompletionClient + Claude implementation), `Ai/SystemPromptBuilder.cs`
- `Messaging/ClaudeReplyGenerator.cs`: restaurant dhoondo → customer upsert → message save → history → Claude → reply save
- `Infrastructure/DatabaseUrl.cs`

## Checklist
- [x] Packages: Anthropic, Npgsql EF Core 8.0.11, EF Design
- [x] Entities + DbContext + migration
- [x] Claude client + system prompt + memory + reply generator
- [x] Program.cs wiring
- [x] Tests: 25 pass (prompt builder, history, reply generator fake AI ke saath, keyed lock, webhook e2e in-memory DB)
- [x] Local Postgres 16 par end-to-end run: migration + seed + 2 messages, memory sahi gayi, `cache_control` + `effort: low` request mein, tokens DB mein (asli Claude ki jagah nakli server, API key nahi thi)
- [x] Per-customer lock (`KeyedLock`): ek customer ke messages ek ke baad ek
- [x] README update (Anthropic key, Postgres setup)
- [ ] **Asli Claude key ke saath test** (Talha ki key aane par): Roman Urdu jawab, prices sahi, cache hit (`CacheReadTokens` > 0 doosre message se)
- [ ] Code `D:\Development\New folder` mein copy (folder share hone ke baad)

## Settings jo Talha ko set karni hain
```
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=chatserve;Username=postgres;Password=..."
```

## Phase 3 ke liye notes (agla qadam)
- Tools: `GetMenu`, `CheckDeliveryArea`, `PlaceOrder` (confirm ke baad), `GetOrderStatus`, `BookTable`, `HandoverToHuman`
- `Orders`, `OrderItems`, `TableBookings` tables + migration
- Tool loop: Haiku 5.5 par forced `tool_choice` nahi hota, `auto` + `strict: true` use karna
- System prompt ki "Online order placement is not live yet" wali line Phase 3 mein badalni hai
