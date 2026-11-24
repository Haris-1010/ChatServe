using ChatServe.Api.Data;
using ChatServe.Api.Messaging;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Dashboard;

/// <summary>Roz ka kaam: overview, live orders, bookings, customers, chats (staff reply, bot pause) aur chat simulator.</summary>
public static class OperationsEndpoints
{
    /// <summary>Simulator ke customers ke number "000" se shuru hote hain. In ko asli WhatsApp nahi bheja jata.</summary>
    public const string SimulatorPhonePrefix = "000";

    public static bool IsSimulated(string phone) => phone.StartsWith(SimulatorPhonePrefix, StringComparison.Ordinal);

    public static void MapOperationsEndpoints(this RouteGroupBuilder api)
    {
        var one = api.MapGroup("/restaurants/{rid:int}");

        // ---- Overview ----
        one.MapGet("/overview", async (int rid, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var pkNow = now.ToOffset(Money.PakistanOffset);
            var dayStart = new DateTimeOffset(pkNow.Date, Money.PakistanOffset).ToUniversalTime(); // Pakistan ki aaj ki raat 12 baje (Postgres ko UTC chahiye)

            var orders = db.Orders.AsNoTracking().Where(o => o.RestaurantId == rid);
            var todayConfirmed = orders.Where(o => o.ConfirmedAt >= dayStart && o.Status != OrderStatus.Cancelled);
            var botToday = db.ChatMessages.AsNoTracking()
                .Where(m => m.RestaurantId == rid && m.CreatedAt >= dayStart && m.Role == ChatRole.Bot);

            var recent = await orders.Where(o => o.Status != OrderStatus.PendingConfirmation)
                .OrderByDescending(o => o.CreatedAt).Take(6)
                .Select(o => new { o.Id, o.CustomerName, o.Status, o.Type, o.Total, o.CreatedAt })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                OrdersToday = await todayConfirmed.CountAsync(ct),
                RevenueToday = await todayConfirmed.SumAsync(o => (decimal?)o.Total, ct) ?? 0,
                ActiveOrders = await orders.CountAsync(o =>
                    o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.OutForDelivery, ct),
                AwaitingConfirmation = await orders.CountAsync(o => o.Status == OrderStatus.PendingConfirmation, ct),
                Customers = await db.Customers.CountAsync(c => c.RestaurantId == rid, ct),
                ChatsToday = await db.Customers.CountAsync(c => c.RestaurantId == rid && c.LastMessageAt >= dayStart, ct),
                BotRepliesToday = await botToday.CountAsync(ct),
                InputTokensToday = await botToday.SumAsync(m => (long?)m.InputTokens, ct) ?? 0,
                OutputTokensToday = await botToday.SumAsync(m => (long?)m.OutputTokens, ct) ?? 0,
                PausedChats = await db.Customers.CountAsync(c => c.RestaurantId == rid && c.BotPausedUntil > now, ct),
                UpcomingBookings = await db.TableBookings.CountAsync(b =>
                    b.RestaurantId == rid && b.BookingTime >= now && b.Status != BookingStatus.Cancelled, ct),
                RecentOrders = recent,
            });
        });

        // ---- Orders ----
        one.MapGet("/orders", async (int rid, string? status, int? days, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var q = db.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.RestaurantId == rid);
            if (status == "active")
                q = q.Where(o => o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.OutForDelivery);
            else if (Enum.TryParse<OrderStatus>(status, true, out var s))
                q = q.Where(o => o.Status == s);
            if (days is > 0)
            {
                var from = clock.GetUtcNow().AddDays(-days.Value);
                q = q.Where(o => o.CreatedAt >= from);
            }
            return await q.OrderByDescending(o => o.CreatedAt).Take(300).ToListAsync(ct);
        });

        one.MapPut("/orders/{id:int}/status", async (int rid, int id, StatusChange<OrderStatus> req, AppDbContext db,
            IWhatsAppClient whatsApp, TimeProvider clock, CancellationToken ct) =>
        {
            var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id && o.RestaurantId == rid, ct);
            if (order is null) return Results.NotFound();
            if (req.Status == OrderStatus.PendingConfirmation)
                return RestaurantEndpoints.Bad("Order wapas 'confirm baqi' mein nahi ja sakta.");

            var now = clock.GetUtcNow();
            order.Status = req.Status;
            order.UpdatedAt = now;
            if (req.Status == OrderStatus.Confirmed && order.ConfirmedAt is null)
                order.ConfirmedAt = now;
            await db.SaveChangesAsync(ct);

            string? notifyError = null;
            if (req.NotifyCustomer)
            {
                var restaurant = await db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == rid, ct);
                var text = OrderStatusMessage(order, restaurant.Currency);
                notifyError = await SendStaffMessageAsync(db, whatsApp, restaurant, order.CustomerId, order.Phone, text, now, ct);
            }
            return Results.Ok(new { order, NotifyError = notifyError });
        });

        // ---- Table bookings ----
        one.MapGet("/bookings", async (int rid, bool? upcoming, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var q = db.TableBookings.AsNoTracking().Where(b => b.RestaurantId == rid);
            if (upcoming == true)
            {
                var from = clock.GetUtcNow().AddHours(-3);
                q = q.Where(b => b.BookingTime >= from);
                return await q.OrderBy(b => b.BookingTime).Take(300).ToListAsync(ct);
            }
            return await q.OrderByDescending(b => b.BookingTime).Take(300).ToListAsync(ct);
        });

        one.MapPut("/bookings/{id:int}/status", async (int rid, int id, StatusChange<BookingStatus> req, AppDbContext db,
            IWhatsAppClient whatsApp, TimeProvider clock, CancellationToken ct) =>
        {
            var booking = await db.TableBookings.FirstOrDefaultAsync(b => b.Id == id && b.RestaurantId == rid, ct);
            if (booking is null) return Results.NotFound();
            booking.Status = req.Status;
            await db.SaveChangesAsync(ct);

            string? notifyError = null;
            if (req.NotifyCustomer && req.Status != BookingStatus.Requested)
            {
                var restaurant = await db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == rid, ct);
                var when = booking.BookingTime.ToOffset(Money.PakistanOffset).ToString("dddd d MMM, h:mm tt");
                var text = req.Status == BookingStatus.Confirmed
                    ? $"✅ {booking.CustomerName}, aap ki table booking confirm ho gayi hai: {when}, {booking.People} afraad. {restaurant.Name} mein aap ka intezar rahega!"
                    : $"Maazrat {booking.CustomerName}, {when} wali table booking cancel kar di gayi hai. Kisi aur waqt ke liye isi chat par bata dein.";
                notifyError = await SendStaffMessageAsync(db, whatsApp, restaurant, booking.CustomerId, booking.Phone, text, clock.GetUtcNow(), ct);
            }
            return Results.Ok(new { booking, NotifyError = notifyError });
        });

        // ---- Customers ----
        one.MapGet("/customers", async (int rid, string? search, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.Customers.AsNoTracking().Where(c => c.RestaurantId == rid);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(c => c.Phone.Contains(s) || (c.Name != null && c.Name.ToLower().Contains(s)));
            }

            var counted = OrderStatusCounted();
            var list = await q.OrderByDescending(c => c.LastMessageAt).Take(500)
                .Select(c => new
                {
                    c.Id, c.Phone, c.Name, c.FirstSeenAt, c.LastMessageAt, c.BotPausedUntil,
                    Orders = db.Orders.Count(o => o.CustomerId == c.Id && counted.Contains(o.Status)),
                    TotalSpent = db.Orders.Where(o => o.CustomerId == c.Id && counted.Contains(o.Status)).Sum(o => (decimal?)o.Total) ?? 0,
                    Messages = db.ChatMessages.Count(m => m.CustomerId == c.Id),
                })
                .ToListAsync(ct);
            return Results.Ok(list.Select(c => new
            {
                c.Id, c.Phone, c.Name, c.FirstSeenAt, c.LastMessageAt, c.BotPausedUntil, c.Orders, c.TotalSpent, c.Messages,
                IsSimulated = IsSimulated(c.Phone),
            }));
        });

        one.MapPost("/customers/{cid:int}/pause", async (int rid, int cid, PauseRequest req, AppDbContext db,
            IOptionsMonitor<OrderOptions> orderOptions, TimeProvider clock, CancellationToken ct) =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == cid && x.RestaurantId == rid, ct);
            if (c is null) return Results.NotFound();
            var hours = req.Hours is > 0 ? req.Hours.Value : orderOptions.CurrentValue.HandoverPauseHours;
            c.BotPausedUntil = clock.GetUtcNow().AddHours(hours);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.BotPausedUntil });
        });

        one.MapPost("/customers/{cid:int}/resume", async (int rid, int cid, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == cid && x.RestaurantId == rid, ct);
            if (c is null) return Results.NotFound();
            c.BotPausedUntil = null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- Chats ----
        one.MapGet("/conversations", async (int rid, AppDbContext db, CancellationToken ct) =>
        {
            var list = await db.Customers.AsNoTracking().Where(c => c.RestaurantId == rid)
                .OrderByDescending(c => c.LastMessageAt).Take(200)
                .Select(c => new
                {
                    c.Id, c.Phone, c.Name, c.LastMessageAt, c.BotPausedUntil,
                    Last = db.ChatMessages.Where(m => m.CustomerId == c.Id)
                        .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                        .Select(m => new { m.Role, m.Text }).FirstOrDefault(),
                })
                .ToListAsync(ct);
            return Results.Ok(list.Select(c => new
            {
                c.Id, c.Phone, c.Name, c.LastMessageAt, c.BotPausedUntil,
                LastRole = c.Last?.Role, LastText = c.Last?.Text,
                IsSimulated = IsSimulated(c.Phone),
            }));
        });

        one.MapGet("/customers/{cid:int}/messages", async (int rid, int cid, AppDbContext db, CancellationToken ct) =>
        {
            var msgs = await db.ChatMessages.AsNoTracking()
                .Where(m => m.RestaurantId == rid && m.CustomerId == cid)
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(300)
                .Select(m => new { m.Id, m.Role, m.Text, m.CreatedAt, m.InputTokens, m.OutputTokens, m.CacheReadTokens })
                .ToListAsync(ct);
            msgs.Reverse();
            return Results.Ok(msgs);
        });

        // Staff khud customer ko jawab deta hai (WhatsApp par restaurant ke number se).
        one.MapPost("/customers/{cid:int}/messages", async (int rid, int cid, StaffReply req, AppDbContext db,
            IWhatsAppClient whatsApp, IOptionsMonitor<OrderOptions> orderOptions, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text)) return RestaurantEndpoints.Bad("Message khali hai.");
            var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == cid && x.RestaurantId == rid, ct);
            if (c is null) return Results.NotFound();
            var restaurant = await db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == rid, ct);
            var now = clock.GetUtcNow();

            if (req.PauseBot)
            {
                c.BotPausedUntil = now.AddHours(orderOptions.CurrentValue.HandoverPauseHours);
                await db.SaveChangesAsync(ct);
            }

            var error = await SendStaffMessageAsync(db, whatsApp, restaurant, c.Id, c.Phone, req.Text.Trim(), now, ct, saveOnFailure: false);
            return error is null
                ? Results.Ok(new { c.BotPausedUntil })
                : Results.Problem(error, statusCode: StatusCodes.Status502BadGateway);
        });

        // ---- Chat simulator: WhatsApp ke baghair bot test karna ----
        one.MapPost("/simulator", async (int rid, SimulatorRequest req, AppDbContext db, IServiceScopeFactory scopes,
            IOptionsMonitor<Configuration.BotOptions> bot, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text)) return RestaurantEndpoints.Bad("Message likhein.");
            var restaurant = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct);
            if (restaurant is null) return Results.NotFound();

            var digits = new string((req.Phone ?? "").Where(char.IsDigit).ToArray());
            var phone = IsSimulated(digits) && digits.Length is >= 6 and <= 20 ? digits : SimulatorPhonePrefix + "000000001";

            await using var scope = scopes.CreateAsyncScope();
            var sim = scope.ServiceProvider.GetRequiredService<SimulationContext>();
            sim.IsActive = true;
            var generator = scope.ServiceProvider.GetRequiredService<IReplyGenerator>();

            var message = new IncomingMessage("sim-" + Guid.NewGuid().ToString("N"), restaurant.WhatsAppPhoneNumberId, phone,
                string.IsNullOrWhiteSpace(req.Name) ? "Test Customer" : req.Name.Trim(), "text", req.Text.Trim(), clock.GetUtcNow());

            var started = clock.GetTimestamp();
            var reply = await generator.GenerateReplyAsync(message, ct);
            var ms = (int)clock.GetElapsedTime(started).TotalMilliseconds;

            string? note = null;
            if (reply is null)
            {
                var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.RestaurantId == rid && c.Phone == phone, ct);
                note = !restaurant.IsBotEnabled ? "Bot is restaurant ke liye OFF hai (Restaurant page)."
                    : customer?.BotPausedUntil > clock.GetUtcNow() ? "Is chat par bot paused hai (staff handover). Chats page se resume karein."
                    : "Bot ne jawab nahi diya.";
            }

            return Results.Ok(new { Phone = phone, Reply = reply, Note = note, StaffAlerts = sim.StaffAlerts, UsedAi = bot.CurrentValue.UseAi, Milliseconds = ms });
        });
    }

    private static OrderStatus[] OrderStatusCounted() =>
        [OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.OutForDelivery, OrderStatus.Completed];

    internal static string OrderStatusMessage(Order o, string currency) => o.Status switch
    {
        OrderStatus.Confirmed => $"✅ Aap ka order *#{o.Id}* confirm ho gaya hai. Total: *{OrderFormatter.TotalText(o, currency)}*",
        OrderStatus.Preparing => $"👨‍🍳 Aap ka order *#{o.Id}* tayyar ho raha hai.",
        OrderStatus.OutForDelivery => $"🛵 Aap ka order *#{o.Id}* rider le kar nikal gaya hai. Total: *{OrderFormatter.TotalText(o, currency)}*",
        OrderStatus.Completed => o.Type == OrderType.Takeaway
            ? $"🎉 Aap ka order *#{o.Id}* tayyar hai, aa kar le jayen. Shukriya!"
            : $"🙏 Order *#{o.Id}* mukammal. Hamein moqa dene ka shukriya! Khana kaisa laga, zaroor batayen.",
        OrderStatus.Cancelled => $"Maazrat, aap ka order *#{o.Id}* cancel kar diya gaya hai. Kisi sawal ke liye isi chat par likhein.",
        _ => $"Order *#{o.Id}* ka status: {o.Status}",
    };

    /// <summary>Staff ki taraf se WhatsApp message + chat log. Simulator customer ko WhatsApp nahi jata, sirf log. Error ho to message wapas.</summary>
    private static async Task<string?> SendStaffMessageAsync(AppDbContext db, IWhatsAppClient whatsApp, Restaurant restaurant,
        int customerId, string phone, string text, DateTimeOffset now, CancellationToken ct, bool saveOnFailure = true)
    {
        string? error = null;
        string? wamid = null;
        if (!IsSimulated(phone))
        {
            try
            {
                wamid = await whatsApp.SendTextAsync(restaurant.WhatsAppPhoneNumberId, phone, text, replyToMessageId: null, ct);
            }
            catch (WhatsAppApiException ex)
            {
                error = "WhatsApp ne message reject kiya" + (ex.ResponseBody?.Contains("131047") == true
                    ? " (24 ghante ki window khatam, ab sirf template ja sakta hai)."
                    : $": {ex.Message}. {ex.ResponseBody}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                error = "WhatsApp tak nahi pohanch sake: " + ex.Message;
            }
        }

        if (error is null || saveOnFailure)
        {
            db.ChatMessages.Add(new ChatMessage
            {
                RestaurantId = restaurant.Id,
                CustomerId = customerId,
                Role = ChatRole.Staff,
                Text = error is null ? text : text + "\n\n[Nahi bheja ja saka]",
                WhatsAppMessageId = wamid,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }
        return error;
    }

    public sealed record StatusChange<T>(T Status, bool NotifyCustomer) where T : struct, Enum;
    public sealed record PauseRequest(int? Hours);
    public sealed record StaffReply(string? Text, bool PauseBot);
    public sealed record SimulatorRequest(string? Phone, string? Name, string? Text);
}
