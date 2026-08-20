using ElBruno.S1Mini.Internal;
using Microsoft.Extensions.AI;

namespace ElBruno.S1Mini;

/// <summary>
/// Self-contained chat client wrapping <c>superwhisper/s1-mini</c> (0.6B Qwen3
/// fine-tune, single-task ASR transcript normalizer — <b>not a general chat model</b>).
/// Downloads the INT4 ONNX conversion from <c>elbruno/s1-mini-onnx</c> on first use
/// and runs generation locally via ONNX Runtime GenAI.
/// <para>
/// This client implements <see cref="IChatClient"/> so it can be composed with
/// Microsoft.Extensions.AI, but it should only ever be used with s1-mini's
/// documented prompt shape (see <see cref="Normalization.TranscriptNormalizer"/>).
/// Feeding it arbitrary chat produces unpredictable output.
/// </para>
/// </summary>
public sealed class S1MiniClient : IChatClient
{
    private readonly S1MiniOptions _options;
    private readonly OnnxGenAIRuntime _runtime;
    private bool _disposed;

    private S1MiniClient(S1MiniOptions options, OnnxGenAIRuntime runtime)
    {
        _options = options;
        _runtime = runtime;
        Metadata = new ChatClientMetadata(
            providerName: "elbruno-s1-mini",
            providerUri: new Uri("https://github.com/elbruno/ElBruno.S1Mini"),
            defaultModelId: "s1-mini");
    }

    /// <summary>Provider metadata.</summary>
    public ChatClientMetadata Metadata { get; }

    /// <summary>
    /// Creates a new <see cref="S1MiniClient"/>, downloading the model on first use
    /// if it is not already cached.
    /// </summary>
    public static async Task<S1MiniClient> CreateAsync(
        S1MiniOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new S1MiniOptions();
        var modelPath = await ModelResolver.ResolveModelPathAsync(options, cancellationToken)
            .ConfigureAwait(false);
        var runtime = new OnnxGenAIRuntime(modelPath);
        return new S1MiniClient(options, runtime);
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);

        var messageList = messages as IList<ChatMessage> ?? [.. messages];
        var prompt = Qwen3PromptBuilder.Build(messageList);

        var parameters = BuildParameters(options);
        var text = await Task.Run(
            () => _runtime.Generate(prompt, parameters, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);

        var messageList = messages as IList<ChatMessage> ?? [.. messages];
        var prompt = Qwen3PromptBuilder.Build(messageList);
        var parameters = BuildParameters(options);

        await foreach (var token in _runtime.GenerateStreamingAsync(prompt, parameters, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, token);
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }
        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.Dispose();
    }

    private static GenerationParameters BuildParameters(ChatOptions? options)
    {
        // Temperature <= 0 => greedy decoding. Do not "fix" this to a positive
        // epsilon: that flips do_sample to true and defeats s1-mini's required
        // greedy decoding.
        var temperature = options?.Temperature ?? 0f;
        var maxOutputTokens = options?.MaxOutputTokens ?? 1024;
        var topP = options?.TopP ?? 0.9f;

        return new GenerationParameters(
            MaxLength: 4096,
            Temperature: temperature,
            TopP: topP,
            TopK: options?.TopK,
            RepetitionPenalty: options?.FrequencyPenalty is float f ? 1.0f + f : 1.0f,
            MaxOutputTokens: maxOutputTokens);
    }
}
