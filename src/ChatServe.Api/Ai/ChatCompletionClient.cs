using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Ai;

public sealed record ChatTurn(bool FromCustomer, string Text);

public sealed record AiResult(
    string? Text, bool Refused, long InputTokens, long OutputTokens, long CacheReadTokens, int ToolCalls = 0, long CacheWriteTokens = 0);

/// <summary>A tool definition. <see cref="JsonSchema"/> is the full JSON schema (type, properties, required, additionalProperties).</summary>
public sealed record AiTool(string Name, string Description, string JsonSchema);

/// <summary>The result of running a tool. IsError tells the AI something went wrong (e.g. a wrong item code).</summary>
public sealed record ToolOutcome(string Content, bool IsError = false);

public interface IToolExecutor
{
    Task<ToolOutcome> ExecuteAsync(string name, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct);
}

/// <summary>Abstraction over the AI model, so tests don't make real API calls.</summary>
public interface IChatCompletionClient
{
    Task<AiResult> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        IReadOnlyList<AiTool> tools,
        IToolExecutor? executor,
        CancellationToken ct);
}

/// <summary>
/// The API key can change from the dashboard, so the client is built per key (not per request:
/// same key, same client).
/// </summary>
public sealed class AnthropicClientProvider(IOptionsMonitor<AiOptions> options)
{
    private readonly object _gate = new();
    private (string? Key, AnthropicClient Client)? _current;

    public static bool HasKey(AiOptions o) =>
        !string.IsNullOrWhiteSpace(o.ApiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    public AnthropicClient Get()
    {
        var key = options.CurrentValue.ApiKey;
        key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        lock (_gate)
        {
            if (_current is { } c && c.Key == key)
                return c.Client;
            // If the key is empty, the SDK reads the ANTHROPIC_API_KEY env var itself.
            var client = key is null ? new AnthropicClient() : new AnthropicClient { ApiKey = key };
            _current = (key, client);
            return client;
        }
    }
}

/// <summary>Claude Messages API (official Anthropic C# SDK) with a manual tool loop.</summary>
public sealed class ClaudeChatCompletionClient(
    AnthropicClientProvider clients,
    IOptionsMonitor<AiOptions> options,
    ILogger<ClaudeChatCompletionClient> logger) : IChatCompletionClient
{
    public async Task<AiResult> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        IReadOnlyList<AiTool> tools,
        IToolExecutor? executor,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var client = clients.Get();

        var messages = new List<MessageParam>();
        foreach (var turn in history)
        {
            messages.Add(turn.FromCustomer
                ? new UserMessageParam { Content = turn.Text }
                : new AssistantMessageParam { Content = turn.Text });
        }

        // Haiku 5.5 doesn't support forced tool_choice: default "auto" + strict schema.
        List<ToolUnion>? toolDefs = tools.Count == 0 || executor is null ? null : tools.Select(ToSdkTool).ToList();

        long input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
        var toolCalls = 0;
        var maxRounds = Math.Max(1, opts.MaxToolRounds);

        for (var round = 0; ; round++)
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = opts.Model,
                MaxTokens = opts.MaxTokens,
                // Tools + restaurant data + rules are the same on every request. This breakpoint
                // caches both tools and system: input cost drops ~90%.
                System = new List<TextBlockParam>
                {
                    new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
                },
                OutputConfig = new OutputConfig { Effort = ParseEffort(opts.Effort) },
                Tools = toolDefs,
                Messages = messages,
            }, cancellationToken: ct);

            input += response.Usage.InputTokens;
            output += response.Usage.OutputTokens;
            cacheRead += response.Usage.CacheReadInputTokens ?? 0;
            cacheWrite += response.Usage.CacheCreationInputTokens ?? 0;

            if (response.StopReason == "refusal")
                return new AiResult(null, true, input, output, cacheRead, toolCalls, cacheWrite); // no server-side fallback on Haiku

            var text = string.Join("\n", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

            if (response.StopReason != "tool_use" || executor is null)
                return new AiResult(string.IsNullOrWhiteSpace(text) ? null : text, false, input, output, cacheRead, toolCalls, cacheWrite);

            if (round + 1 >= maxRounds)
            {
                logger.LogWarning("Tool loop stopped after {Rounds} rounds", maxRounds);
                return new AiResult(null, false, input, output, cacheRead, toolCalls, cacheWrite);
            }

            // The full assistant reply (thinking + tool_use) must be sent back, then all tool results in one user message.
            var assistantContent = new List<ContentBlockParam>();
            var results = new List<ContentBlockParam>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? t))
                {
                    assistantContent.Add(new TextBlockParam { Text = t.Text });
                }
                else if (block.TryPickThinking(out ThinkingBlock? th))
                {
                    assistantContent.Add(new ThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
                }
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? rt))
                {
                    assistantContent.Add(new RedactedThinkingBlockParam { Data = rt.Data });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? tu))
                {
                    assistantContent.Add(new ToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });

                    toolCalls++;
                    ToolOutcome outcome;
                    try
                    {
                        outcome = await executor.ExecuteAsync(tu.Name, tu.Input, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Tool {Tool} failed", tu.Name);
                        outcome = new ToolOutcome("Internal error while running this tool. Tell the customer a staff member will help them.", true);
                    }

                    logger.LogInformation("Tool {Tool} -> error={IsError}", tu.Name, outcome.IsError);
                    results.Add(new ToolResultBlockParam { ToolUseID = tu.ID, Content = outcome.Content, IsError = outcome.IsError });
                }
            }

            messages.Add(new AssistantMessageParam { Content = assistantContent });
            messages.Add(new UserMessageParam { Content = results });
        }
    }

    private static ToolUnion ToSdkTool(AiTool tool)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tool.JsonSchema)
                     ?? throw new InvalidOperationException($"Invalid schema for tool {tool.Name}");
        return new Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = InputSchema.FromRawUnchecked(schema),
            Strict = true, // input always matches the schema
        };
    }

    private static Effort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        _ => Effort.Low,
    };
}
