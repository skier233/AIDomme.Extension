using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace AIDome.Services;

/// <summary>
/// Mutable TTS settings. Registered as a singleton in the extension container and
/// updated from the persisted extension store (and the settings panel) at runtime.
/// </summary>
public sealed class TtsOptions
{
    public const string SectionName = "Tts";

    /// <summary>Base URL of the Orpheus-FastAPI server (e.g. "http://localhost:5005"). Blank disables voice.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Voice to use. Orpheus English voices: tara, leah, jess, leo, dan, mia, zac, zoe.</summary>
    public string Voice { get; set; } = "mia";

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Talks to an Orpheus-FastAPI server to synthesize WAV speech. Reads the current
/// <see cref="TtsOptions"/> on every call so settings changes apply without a restart.
/// Returns null (and reports unavailable) when no server is configured or reachable.
/// </summary>
public sealed class OrpheusTtsService : ITtsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TtsOptions _options;
    private readonly ILogger<OrpheusTtsService> _logger;
    private bool _available;

    public OrpheusTtsService(IHttpClientFactory httpClientFactory, TtsOptions options, ILogger<OrpheusTtsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public bool IsAvailable => _available && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public async Task<byte[]?> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_options.BaseUrl))
            return null;

        try
        {
            using var http = _httpClientFactory.CreateClient("orpheus-tts");
            http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/'));
            http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds <= 0 ? 30 : _options.TimeoutSeconds);

            var payload = new
            {
                model = "orpheus",
                input = text,
                voice = string.IsNullOrWhiteSpace(_options.Voice) ? "mia" : _options.Voice,
                response_format = "wav",
                speed = 1.0
            };

            using var response = await http.PostAsJsonAsync("/v1/audio/speech", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Orpheus TTS returned {Status}", response.StatusCode);
                return null;
            }

            var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            _available = true;
            return audio;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Orpheus TTS request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Orpheus TTS server unreachable");
            _available = false;
            return null;
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Orpheus TTS base URL is invalid: {BaseUrl}", _options.BaseUrl);
            _available = false;
            return null;
        }
    }
}
