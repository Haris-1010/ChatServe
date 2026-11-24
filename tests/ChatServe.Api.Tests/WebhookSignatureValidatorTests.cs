using System.Text;
using ChatServe.Api.WhatsApp;

namespace ChatServe.Api.Tests;

public class WebhookSignatureValidatorTests
{
    private const string Secret = "test-app-secret";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\"}");

    [Fact]
    public void Accepts_correct_signature() =>
        Assert.True(WebhookSignatureValidator.IsValid(Body, WebhookSignatureValidator.ComputeHeader(Body, Secret), Secret));

    [Fact]
    public void Rejects_signature_from_other_secret() =>
        Assert.False(WebhookSignatureValidator.IsValid(Body, WebhookSignatureValidator.ComputeHeader(Body, "other"), Secret));

    [Fact]
    public void Rejects_tampered_body()
    {
        var header = WebhookSignatureValidator.ComputeHeader(Body, Secret);
        var tampered = Encoding.UTF8.GetBytes("{\"object\":\"hacked\"}");
        Assert.False(WebhookSignatureValidator.IsValid(tampered, header, Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=nothex")]
    [InlineData("md5=abcd")]
    public void Rejects_missing_or_malformed_header(string? header) =>
        Assert.False(WebhookSignatureValidator.IsValid(Body, header, Secret));
}
