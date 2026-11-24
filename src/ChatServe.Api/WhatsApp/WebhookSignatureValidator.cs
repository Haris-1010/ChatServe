using System.Security.Cryptography;
using System.Text;

namespace ChatServe.Api.WhatsApp;

/// <summary>
/// Meta har webhook POST ko App Secret se sign karta hai:
/// header "X-Hub-Signature-256: sha256=&lt;hex HMAC-SHA256 of raw body&gt;".
/// Is se pakka hota hai ke request waqai Meta se aayi hai, kisi aur ne nakli order nahi bheja.
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

    /// <summary>Tests aur local curl testing ke liye header value banata hai.</summary>
    public static string ComputeHeader(ReadOnlySpan<byte> rawBody, string appSecret) =>
        Prefix + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), rawBody)).ToLowerInvariant();
}
