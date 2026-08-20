using ElBruno.S1Mini.Normalization;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.S1Mini;

/// <summary>
/// DI registration for <see cref="S1MiniClient"/> and <see cref="TranscriptNormalizer"/>.
/// </summary>
public static class S1MiniServiceExtensions
{
    /// <summary>
    /// Registers a <see cref="TranscriptNormalizer"/> singleton backed by a fresh
    /// <see cref="S1MiniClient"/>. The client is created lazily on first resolution
    /// and downloads the model then; subsequent resolutions reuse it.
    /// <para>
    /// <see cref="TranscriptNormalizer"/> is deliberately <b>not</b> registered as
    /// <c>IChatClient</c>: s1-mini is not a general-purpose chat model and
    /// registering it as one would mislead consumers expecting chat semantics.
    /// </para>
    /// </summary>
    public static IServiceCollection AddTranscriptNormalizer(
        this IServiceCollection services,
        Action<S1MiniOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new S1MiniOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<S1MiniOptions>();
            // Block the DI thread once at first resolution; subsequent gets are cached.
            return TranscriptNormalizer.CreateAsync(opts).GetAwaiter().GetResult();
        });

        return services;
    }
}
