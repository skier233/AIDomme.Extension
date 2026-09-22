namespace AIDome.Services;

public interface ITtsService
{
    /// <summary>
    /// Synthesize speech from text and return WAV audio bytes.
    /// Returns null if TTS is unavailable or disabled.
    /// </summary>
    Task<byte[]?> SynthesizeAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Whether the TTS backend is configured and reachable.</summary>
    bool IsAvailable { get; }
}
