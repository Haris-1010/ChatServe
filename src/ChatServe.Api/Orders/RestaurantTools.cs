using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;

namespace ChatServe.Api.Orders;

/// <summary>
/// The AI's tools. Definitions are deterministic per restaurant (delivery areas in an enum),
/// so the prompt cache (tools + system) keeps hitting.
/// No separate GetMenu tool: the full menu is already in the (cached) system prompt, which saves an extra round trip.
/// The delivery area check is inside place_order (enum + server validation).
/// </summary>
public static class RestaurantTools
{
    public const string PlaceOrder = "place_order";
    public const string GetOrderStatus = "get_order_status";
    public const string BookTable = "book_table";
    public const string HandoverToHuman = "handover_to_human";
    public const string ShowOptions = "show_options";

    /// <summary>Ids of the choices the AI puts under its reply: "opt:1", "opt:2"... The tap comes back as the option's title.</summary>
    public const string OptionIdPrefix = "opt:";

    public const string NoArea = "none";

    public static IReadOnlyList<AiTool> Build(Restaurant r)
    {
        var areas = r.DeliveryZones.Where(z => z.IsActive).OrderBy(z => z.Id).Select(z => z.Name).Append(NoArea).ToArray();
        var terms = BusinessTerms.For(r.BusinessType);
        var orderTypes = SystemPromptBuilder.OrderTypes(r, areas.Length > 1).Select(x => x.Value).ToArray();

        var placeOrder = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["items"] = new
                {
                    type = "array",
                    description = "Every item in the order (the full order, not just changes).",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["code"] = new { type = "string", description = "Item code from the list, e.g. M3 or D1." },
                            ["quantity"] = new { type = "integer", description = "How many, 1 or more." },
                            ["note"] = new { type = "string", description = "Special request for this item (e.g. less spicy), or empty string." },
                        },
                        required = new[] { "code", "quantity", "note" },
                        additionalProperties = false,
                    },
                },
                ["order_type"] = new { type = "string", @enum = orderTypes, description = "takeaway = the customer picks it up." },
                ["customer_name"] = new { type = "string", description = "Name the customer gave for the order." },
                ["delivery_address"] = new { type = "string", description = "Full delivery address. Empty string if not delivery." },
                ["delivery_area"] = new { type = "string", @enum = areas, description = $"Delivery area from the list. \"{NoArea}\" if not delivery." },
                ["payment_method"] = new { type = "string", description = "Payment method the customer chose; empty string if they didn't say." },
                ["notes"] = new { type = "string", description = "Any other instruction for the whole order, or empty string." },
                ["address_confirmed"] = new { type = "boolean", description = "false normally. true only after place_order said the address looks like a different place and the customer then confirmed the address and area are correct." },
            },
            required = new[] { "items", "order_type", "customer_name", "delivery_address", "delivery_area", "payment_method", "notes", "address_confirmed" },
            additionalProperties = false,
        };

        var empty = new { type = "object", properties = new Dictionary<string, object>(), required = Array.Empty<string>(), additionalProperties = false };

        var bookTable = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["customer_name"] = new { type = "string" },
                ["date"] = new { type = "string", description = "YYYY-MM-DD (Pakistan time). Work it out from the current time note." },
                ["time"] = new { type = "string", description = "24-hour HH:MM, e.g. 20:30." },
                ["people"] = new { type = "integer" },
                ["notes"] = new { type = "string", description = terms.BookingHasPeople ? "Any request (family hall, birthday, high chair), or empty string." : "What the booking is for (e.g. the service) and any request, or empty string." },
            },
            required = new[] { "customer_name", "date", "time", "people", "notes" },
            additionalProperties = false,
        };

        var handover = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["reason"] = new { type = "string", description = "Short reason for the staff, e.g. \"Complaint: cold food on order #12\"." },
            },
            required = new[] { "reason" },
            additionalProperties = false,
        };

        var showOptions = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["options"] = new
                {
                    type = "array",
                    description = "1 to 10 choices in the order to show them.",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["title"] = new { type = "string", description = "What the customer taps. Max 20 characters; an emoji in front is nice (e.g. \"🛵 Delivery\")." },
                            ["description"] = new { type = "string", description = "Small grey line under the title in a list, e.g. the price \"Rs 500\" or \"Rs 200 delivery\". Empty string if not needed. Max 70 characters." },
                        },
                        required = new[] { "title", "description" },
                        additionalProperties = false,
                    },
                },
                ["list_button"] = new { type = "string", description = "Only for 4 or more options: the text on the button that opens the list (max 20), e.g. \"See menu\". Empty string otherwise." },
            },
            required = new[] { "options", "list_button" },
            additionalProperties = false,
        };

        var tools = new List<AiTool>();
        if (r.TakesOrders)
        {
            tools.Add(new AiTool(PlaceOrder,
                "Create or replace the customer's order draft once you know the items, order type, the customer's name and, for delivery, " +
                "the full address and area. The system calculates prices and totals and returns the order summary. The order is NOT sent to " +
                "the team until the customer replies \"confirm\". Call again with the full updated order if the customer changes anything.",
                Json(placeOrder)));
        }
        tools.Add(new AiTool(GetOrderStatus,
            "Get the status of this customer's recent orders. Use when they ask where their order is or about an order they placed.",
            Json(empty)));
        if (r.TakesBookings)
        {
            tools.Add(new AiTool(BookTable,
                $"Request a {terms.BookingWord} once you have the name, date, time{(terms.BookingHasPeople ? " and number of people" : "")}. The team will confirm it.",
                Json(bookTable)));
        }
        tools.Add(new AiTool(HandoverToHuman,
            "Hand the chat to a team member: complaints, refunds, cancelling or changing an already confirmed order, bulk orders, " +
            "the customer asks for a human, or anything you can't handle. The bot stops replying to this customer after this.",
            Json(handover)));
        tools.Add(new AiTool(ShowOptions,
            "Show tappable choices under your reply (WhatsApp buttons for up to 3 short options, a list for 4 to 10). Use it whenever the " +
            "customer has to pick something: a category, an item, quantity, order type, delivery area, payment method, yes/no. " +
            "The customer's tap comes back to you as the option's title. Always also write a short reply text.",
            Json(showOptions)));
        return tools;
    }

    private static string Json(object schema) => JsonSerializer.Serialize(schema);
}

/// <summary>
/// Runs tools for one customer message. Remembers side effects (draft created, what to send staff, handover)
/// so the reply generator can act on them after the AI call.
/// </summary>
public sealed class OrderToolExecutor(
    OrderService orders, Restaurant restaurant, Customer customer, DateTimeOffset now, bool romanUrdu = false) : IToolExecutor
{
    public Order? Draft { get; private set; }
    public bool HandedOver { get; private set; }

    /// <summary>Choices the AI asked to show under its reply (show_options), or null.</summary>
    public InteractiveMessage? Options { get; private set; }
    public List<StaffAlert> StaffAlerts { get; } = [];

    public async Task<ToolOutcome> ExecuteAsync(string name, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        switch (name)
        {
            case RestaurantTools.PlaceOrder:
            {
                var type = Str(input, "order_type") switch
                {
                    "delivery" => OrderType.Delivery,
                    "takeaway" => OrderType.Takeaway,
                    _ => OrderType.DineIn,
                };
                var items = input.TryGetValue("items", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(i => new DraftItemRequest(
                        i.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                        i.TryGetProperty("quantity", out var q) && q.TryGetInt32(out var n) ? n : 0,
                        i.TryGetProperty("note", out var note) ? note.GetString() : null)).ToList()
                    : [];
                var area = Str(input, "delivery_area");

                var result = await orders.SaveDraftAsync(restaurant, customer, new DraftRequest(
                    items, type, Str(input, "customer_name"), Str(input, "delivery_address"),
                    area == RestaurantTools.NoArea ? null : area, Str(input, "payment_method"), Str(input, "notes"),
                    AddressConfirmed: input.TryGetValue("address_confirmed", out var ac) && ac.ValueKind == JsonValueKind.True), now, ct);

                if (result.Value is null)
                    return new ToolOutcome(result.Error!, IsError: true);

                Draft = result.Value;
                return new ToolOutcome(
                    "Draft saved, NOT confirmed yet. Send the customer this summary now. You may translate it to the customer's language, " +
                    "but keep every item, quantity and amount exactly as written:\n\n" +
                    OrderFormatter.DraftSummary(Draft, restaurant.Currency, romanUrdu));
            }

            case RestaurantTools.GetOrderStatus:
            {
                var recent = await orders.GetRecentOrdersAsync(customer.Id, 3, ct);
                var pending = Draft ?? await orders.GetPendingDraftAsync(customer.Id, now, ct);
                var lines = recent.Select(o =>
                    $"Order #{o.Id} ({o.CreatedAt.ToOffset(Money.PakistanOffset):d MMM h:mm tt}, {OrderFormatter.TypeText(o.Type)}, " +
                    $"{OrderFormatter.TotalText(o, restaurant.Currency)}): {OrderFormatter.StatusText(o.Status)}").ToList();
                if (pending is not null)
                    lines.Insert(0, $"Unconfirmed draft ({OrderFormatter.TotalText(pending, restaurant.Currency)}): waiting for the customer to reply \"confirm\".");
                return new ToolOutcome(lines.Count == 0
                    ? "This customer has no orders yet."
                    : string.Join("\n", lines) + "\nFor exact delivery time or problems, offer to hand over to staff.");
            }

            case RestaurantTools.BookTable:
            {
                var people = input.TryGetValue("people", out var p) && p.TryGetInt32(out var n) ? n : 0;
                var result = await orders.BookTableAsync(restaurant, customer, Str(input, "customer_name"),
                    Str(input, "date"), Str(input, "time"), people, Str(input, "notes"), now, ct);
                if (result.Value is null)
                    return new ToolOutcome(result.Error!, IsError: true);

                StaffAlerts.Add(new StaffAlert(OrderFormatter.BookingTicket(result.Value), StaffButtons.ForBooking(result.Value)));
                return new ToolOutcome(
                    $"Booking request #{result.Value.Id} saved for {result.Value.BookingTime:dddd d MMMM, h:mm tt}, {result.Value.People} people. " +
                    "Tell the customer the request is received and the staff will confirm it shortly. It is not confirmed yet.");
            }

            case RestaurantTools.HandoverToHuman:
            {
                var reason = Str(input, "reason");
                await orders.PauseBotAsync(customer, now, ct);
                HandedOver = true;
                StaffAlerts.Add(OrderFormatter.HandoverTicket(customer, string.IsNullOrWhiteSpace(reason) ? "(not given)" : reason));
                return new ToolOutcome(
                    "Staff has been notified and will continue this chat. Tell the customer briefly that a team member will reply soon. Don't ask further questions.");
            }

            case RestaurantTools.ShowOptions:
            {
                var options = input.TryGetValue("options", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray()
                        .Select(o => (Title: (o.TryGetProperty("title", out var t) ? t.GetString() : null)?.Trim() ?? "",
                                      Description: (o.TryGetProperty("description", out var d) ? d.GetString() : null)?.Trim()))
                        .Where(o => o.Title.Length > 0)
                        .DistinctBy(o => o.Title, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : [];
                if (options.Count == 0)
                    return new ToolOutcome("Give at least one option with a title.", IsError: true);
                if (options.Count > InteractiveMessage.MaxRows)
                    return new ToolOutcome($"At most {InteractiveMessage.MaxRows} options fit. Show the most useful {InteractiveMessage.MaxRows} and say they can type any other.", IsError: true);

                var asButtons = options.Count <= InteractiveMessage.MaxButtons && options.All(o => o.Title.Length <= 20) &&
                                options.All(o => string.IsNullOrEmpty(o.Description));
                var listButton = Str(input, "list_button").Trim();
                Options = new InteractiveMessage(
                    options.Select((o, i) => new ReplyOption(RestaurantTools.OptionIdPrefix + (i + 1), o.Title,
                        string.IsNullOrEmpty(o.Description) ? null : o.Description)).ToList(),
                    ListButton: asButtons ? null : listButton.Length > 0 ? listButton : romanUrdu ? "Options dekhein" : "See options");
                return new ToolOutcome(
                    "Done: these will show as tappable choices under your reply. Now write the reply text, short. " +
                    "Don't repeat the options as a numbered list (the prices you put in descriptions are visible in the list).");
            }

            default:
                return new ToolOutcome($"Unknown tool {name}.", IsError: true);
        }
    }

    private static string Str(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
