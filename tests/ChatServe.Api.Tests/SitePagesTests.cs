using System.Net;

namespace ChatServe.Api.Tests;

public class SitePagesTests
{
    [Fact]
    public async Task Public_pages_show_business_details_without_login_and_encode_them()
    {
        await using var factory = new DashboardApiTests.Factory();
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Site:BusinessName", "ChatServe");
            b.UseSetting("Site:Address", "House 1, <Street> 2, Lahore");
            b.UseSetting("Site:ContactEmail", "hello@example.com");
        }).CreateClient();

        foreach (var path in new[] { "/site", "/site/privacy", "/site/terms" })
        {
            var res = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
            var html = await res.Content.ReadAsStringAsync();
            Assert.Contains("ChatServe", html);
            Assert.Contains("House 1, &lt;Street&gt; 2, Lahore", html);
            Assert.DoesNotContain("<Street>", html);
            var dump = Environment.GetEnvironmentVariable("SITE_DUMP_DIR");
            if (!string.IsNullOrEmpty(dump))
                await File.WriteAllTextAsync(Path.Combine(dump, (path == "/site" ? "home" : path[6..]) + ".html"), html);
        }

        var privacy = await client.GetStringAsync("/site/privacy");
        Assert.Contains("mailto:hello@example.com", privacy);
        Assert.Contains("OpenAI", privacy);
    }
}
