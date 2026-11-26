using System.Collections.Concurrent;
using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;
using ChatServe.Api.Orders;

namespace ChatServe.Api.Tests;

public sealed class FakeChatCompletionClient : IChatCompletionClient
{
    public ConcurrentQueue<(string System, IReadOnlyList<ChatTurn> History, IReadOnlyList<AiTool> Tools)> Calls { get; } = new();
    public Func<IReadOnlyList<ChatTurn>, AiResult>? Responder { get; set; }

    /// <summary>Runs tools like real Claude would: the test calls the executor itself.</summary>
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

    /// <summary>Converts tool input into Claude's shape (a dictionary of JsonElement).</summary>
    public static IReadOnlyDictionary<string, JsonElement> Input(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;
}

public sealed class FakeStaffNotifier : IStaffNotifier
{
    public ConcurrentQueue<string> Alerts { get; } = new();
    public ConcurrentQueue<(string Text, InteractiveMessage? Buttons)> WithButtons { get; } = new();

    public Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct, string? exceptPhone = null, InteractiveMessage? buttons = null)
    {
        Alerts.Enqueue(text);
        WithButtons.Enqueue((text, buttons));
        return Task.CompletedTask;
    }
}
