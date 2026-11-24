using System.Collections.Concurrent;
using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;

namespace ChatServe.Api.Tests;

public sealed class FakeChatCompletionClient : IChatCompletionClient
{
    public ConcurrentQueue<(string System, IReadOnlyList<ChatTurn> History, IReadOnlyList<AiTool> Tools)> Calls { get; } = new();
    public Func<IReadOnlyList<ChatTurn>, AiResult>? Responder { get; set; }

    /// <summary>Asli Claude ki tarah tools chalane ke liye: test khud executor ko call karta hai.</summary>
    public Func<IReadOnlyList<ChatTurn>, IToolExecutor, Task<AiResult>>? ToolScript { get; set; }

    public async Task<AiResult> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history,
        IReadOnlyList<AiTool> tools, IToolExecutor? executor, CancellationToken ct)
    {
        Calls.Enqueue((systemPrompt, history, tools));
        if (ToolScript is not null && executor is not null)
            return await ToolScript(history, executor);

        return Responder?.Invoke(history)
               ?? new AiResult("AI: " + history[^1].Text.Split("\n\n[Current time")[0], false, 1000, 50, 900);
    }

    /// <summary>Tool input ko Claude jaisi shakal (dictionary of JsonElement) mein badalta hai.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Input(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;
}

public sealed class FakeStaffNotifier : IStaffNotifier
{
    public ConcurrentQueue<string> Alerts { get; } = new();

    public Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct)
    {
        Alerts.Enqueue(text);
        return Task.CompletedTask;
    }
}
