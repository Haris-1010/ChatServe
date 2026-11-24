using System.Text.Json;
using ChatServe.Api.Messaging;
using ChatServe.Api.WhatsApp.Models;

namespace ChatServe.Api.Tests;

public class WebhookParserTests
{
    [Fact]
    public void Extracts_text_message_with_name_and_phone_number_id()
    {
        var payload = JsonSerializer.Deserialize<WebhookPayload>(SamplePayloads.TextMessage("Menu bhej dein"))!;

        var messages = WebhookParser.ExtractMessages(payload, "fallback");

        var m = Assert.Single(messages);
        Assert.Equal("wamid.TEST1", m.MessageId);
        Assert.Equal("111222333", m.PhoneNumberId);
        Assert.Equal("923001234567", m.From);
        Assert.Equal("Ali", m.CustomerName);
        Assert.Equal("text", m.Type);
        Assert.Equal("Menu bhej dein", m.Text);
    }

    [Fact]
    public void Ignores_status_updates()
    {
        var payload = JsonSerializer.Deserialize<WebhookPayload>(SamplePayloads.StatusUpdate)!;

        Assert.Empty(WebhookParser.ExtractMessages(payload, "fallback"));
    }

    [Fact]
    public void Non_text_message_has_null_text()
    {
        var json = SamplePayloads.TextMessage("x").Replace("\"type\": \"text\"", "\"type\": \"image\"");
        var payload = JsonSerializer.Deserialize<WebhookPayload>(json)!;

        var m = Assert.Single(WebhookParser.ExtractMessages(payload, "fallback"));
        Assert.Equal("image", m.Type);
        Assert.Null(m.Text);
    }
}
