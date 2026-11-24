using ChatServe.Api.Data;
using ChatServe.Api.Messaging;

namespace ChatServe.Api.Tests;

public class HistoryTests
{
    [Fact]
    public void Drops_leading_bot_messages_and_merges_same_role()
    {
        var turns = ClaudeReplyGenerator.BuildTurns(new[]
        {
            (ChatRole.Bot, "purana bot msg"),
            (ChatRole.Customer, "salam"),
            (ChatRole.Customer, "menu?"),
            (ChatRole.Bot, "ji menu ye hai"),
            (ChatRole.Staff, "staff note"),
            (ChatRole.Customer, "shukriya"),
        });

        Assert.Equal(3, turns.Count);
        Assert.True(turns[0].FromCustomer);
        Assert.Equal("salam\nmenu?", turns[0].Text);
        Assert.False(turns[1].FromCustomer);
        Assert.Equal("ji menu ye hai\nstaff note", turns[1].Text);
        Assert.Equal("shukriya", turns[2].Text);
    }

    [Fact]
    public void Adds_pakistan_time_to_last_customer_turn_only()
    {
        var turns = ClaudeReplyGenerator.BuildTurns(new[] { (ChatRole.Customer, "a"), (ChatRole.Bot, "b"), (ChatRole.Customer, "abhi khula hai?") });
        var utc = new DateTimeOffset(2026, 11, 24, 16, 30, 0, TimeSpan.Zero); // 9:30 PM PKT

        var result = ClaudeReplyGenerator.WithCurrentTime(turns, utc);

        Assert.Equal("a", result[0].Text);
        Assert.StartsWith("abhi khula hai?", result[2].Text);
        Assert.Contains("Tuesday 24 November 2026, 9:30 PM", result[2].Text);
    }
}
