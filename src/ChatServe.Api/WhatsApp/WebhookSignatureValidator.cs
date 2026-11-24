using System.Security.Cryptography;
using System.Text;

namespace ChatServe.Api.WhatsApp;

/// <summary>
/// Meta signs every webhook POST with the App Secret:
/// header "X-Hub-Signature-256: sha256=&lt;hex HMAC-SHA256 of raw body&gt;".
/// This proves the request really came from Meta and nobody sent a fake order.
/// </summary>
public static class WebhookSignatureValidator
{
    public const string HeaderName = "X-Hub-Signature-256";
    private const string Prefix = "sha256=";

    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(appSecret))
            return false;

        if (!signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(signatureHeader.AsSpan(Prefix.Length).Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), rawBody);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Builds the header value for tests and local curl testing.</summary>
    public static string ComputeHeader(ReadOnlySpan<byte> rawBody, string appSecret) =>
        Prefix + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), rawBody)).ToLowerInvariant();
}
