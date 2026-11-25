using System.Net.Http.Headers;
using ChatServe.Api.WhatsApp;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Messaging;

public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    /// <summary>OpenAI API key for Whisper (platform.openai.com). Empty = voice notes get a "please type" reply.</summary>
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>OpenAI speech-to-text model.</summary>
    public string Model { get; set; } = "whisper-1";
}

/// <summary>Turns a customer's voice note into text, so the bot can answer it like a typed message.</summary>
public interface IVoiceTranscriber
{
    bool IsConfigured { get; }

    /// <summary>The spoken text, or null if nothing could be understood.</summary>
    Task<string?> TranscribeAsync(MediaFile audio, CancellationToken ct);
}

/// <summary>OpenAI Whisper over plain HTTP (multipart upload to /v1/audio/transcriptions).</summary>
public sealed class WhisperVoiceTranscriber(HttpClient http, IOptionsMonitor<VoiceOptions> options) : IVoiceTranscriber
{
    private const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";

    // A hint in the style customers speak and write, so names of dishes and Roman Urdu come out right.
    private const string StyleHint = "Assalam o alaikum. Mujhe 2 chicken biryani aur 1 zinger burger chahiye, delivery Johar Town mein. Order kab tak aaye ga?";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.OpenAiApiKey);

    public async Task<string?> TranscribeAsync(MediaFile audio, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio.Data);
        var mime = audio.MimeType.Split(';')[0].Trim();
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "file", "voice" + Extension(mime));
        form.Add(new StringContent(opts.Model), "model");
        form.Add(new StringContent("text"), "response_format");
        form.Add(new StringContent(StyleHint), "prompt");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.OpenAiApiKey.Trim());
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Whisper returned {(int)response.StatusCode}: {Truncate(body)}");

        var text = body.Trim();
        return text.Length == 0 ? null : text;
    }

    private static string Extension(string mime) => mime switch
    {
        "audio/ogg" or "audio/opus" => ".ogg",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a",
        "audio/aac" => ".aac",
        "audio/amr" => ".amr",
        "audio/wav" or "audio/x-wav" => ".wav",
        "audio/webm" => ".webm",
        _ => ".ogg",
    };

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
