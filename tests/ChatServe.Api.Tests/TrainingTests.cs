using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;

namespace ChatServe.Api.Tests;

public sealed class FakeScreenshotReader : IChatScreenshotReader
{
    public string? Result { get; set; } = "Customer: menu bhej dein\nRestaurant: Ji zaroor sir!";
    public int Calls { get; private set; }

    public Task<string?> ReadAsync(byte[] image, string mediaType, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

public class TrainingTests
{
    [Fact]
    public void Android_export_becomes_plain_lines()
    {
        const string export = """
            25/11/2026, 12:20 pm - Messages and calls are end-to-end encrypted. No one outside of this chat can read them.
            25/11/2026, 12:21 pm - Ali Khan: Assalam o alaikum
            menu bhej dein
            25/11/2026, 12:21 pm - Ali Khan: <Media omitted>
            25/11/2026, 12:22 pm - Karachi Grill: Walaikum assalam bhai! Ji zaroor
            25/11/2026, 12:23 pm - Karachi Grill: Mera number 0300-1234567 hai
            """;

        var text = TrainingText.Clean(TrainingText.FromWhatsAppExport(export));

        Assert.Equal(
            "Ali Khan: Assalam o alaikum\nmenu bhej dein\nKarachi Grill: Walaikum assalam bhai! Ji zaroor\nKarachi Grill: Mera number [number] hai",
            text);
    }

    [Fact]
    public void Iphone_export_is_parsed_and_phone_numbers_masked()
    {
        const string export = "[25/11/2026, 12:21:05 PM] Ali: call me on +92 300 1234567\n‎[25/11/2026, 12:21:09 PM] Ali: ‎image omitted\n[25/11/2026, 12:22:00 PM] Grill: Ji bhai";

        Assert.Equal("Ali: call me on [number]\nGrill: Ji bhai", TrainingText.Clean(TrainingText.FromWhatsAppExport(export)));
    }

    [Fact]
    public void Pasted_text_is_kept_as_it_is()
    {
        const string pasted = "Customer: 2 biryani\nRestaurant: Ji done! Rs 900 total";
        Assert.Equal(pasted, TrainingText.FromWhatsAppExport(pasted));
        Assert.Equal(pasted, TrainingText.Clean(pasted)); // prices are not phone numbers
    }

    [Fact]
    public void Examples_go_into_the_system_prompt()
    {
        var r = new Restaurant { Name = "R", WhatsAppPhoneNumberId = "1", TrainingExamples = "Restaurant: Ji sir, hukum karein" };
        var prompt = SystemPromptBuilder.Build(r);
        Assert.Contains("<examples>\nRestaurant: Ji sir, hukum karein\n</examples>", prompt);
        Assert.DoesNotContain("<examples>", SystemPromptBuilder.Build(new Restaurant { Name = "R", WhatsAppPhoneNumberId = "1" }));
    }

    [Fact]
    public async Task Owner_imports_a_screenshot_and_an_export_then_saves()
    {
        var (factory, c) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var rid = await DashboardApiTests.RestaurantIdAsync(c);

        var fromImage = await c.PostAsJsonAsync($"/api/restaurants/{rid}/training/import",
            new { fileName = "chat.png", imageBase64 = Convert.ToBase64String([1, 2, 3]), mediaType = "image/png" });
        fromImage.EnsureSuccessStatusCode();
        Assert.Contains("Ji zaroor sir", (await fromImage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString());

        var fromTxt = await c.PostAsJsonAsync($"/api/restaurants/{rid}/training/import",
            new { fileName = "chat.txt", text = "25/11/2026, 12:21 pm - Ali: hello 03001234567" });
        Assert.Equal("Ali: hello [number]", (await fromTxt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString());

        var badType = await c.PostAsJsonAsync($"/api/restaurants/{rid}/training/import",
            new { fileName = "x.pdf", imageBase64 = "AAAA", mediaType = "application/pdf" });
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);

        (await c.PutAsJsonAsync($"/api/restaurants/{rid}/training", new { text = "Restaurant: Ji bhai\n\n\n\nCustomer: ok" })).EnsureSuccessStatusCode();
        var saved = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/training");
        Assert.Equal("Restaurant: Ji bhai\n\nCustomer: ok", saved.GetProperty("text").GetString());

        var tooLong = await c.PutAsJsonAsync($"/api/restaurants/{rid}/training", new { text = new string('a', TrainingText.MaxLength + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }
}
