using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace ElBruno.S1Mini.Internal;

/// <summary>
/// Generation parameters for the ORT-GenAI runtime. <see cref="Temperature"/>
/// less than or equal to 0 selects greedy decoding.
/// </summary>
internal sealed record GenerationParameters(
    int MaxLength = 2048,
    float Temperature = 0f,
    float TopP = 0.9f,
    int? TopK = null,
    float RepetitionPenalty = 1.0f,
    int? MaxOutputTokens = null);

/// <summary>
/// Thin wrapper around ONNX Runtime GenAI for model loading and inference.
/// Owns the <see cref="Model"/> and <see cref="Tokenizer"/> lifecycles.
/// </summary>
internal sealed class OnnxGenAIRuntime : IDisposable
{
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private bool _disposed;

    public OnnxGenAIRuntime(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _model = new Model(modelPath);
        _tokenizer = new Tokenizer(_model);
    }

    public string Generate(string prompt, GenerationParameters parameters, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        using var sequences = _tokenizer.Encode(prompt);
        var inputTokenCount = sequences[0].Length;

        using var genParams = new GeneratorParams(_model);
        ApplyParameters(new OnnxGenerationSearchOptions(genParams), parameters, inputTokenCount);

        using var generator = new Generator(_model, genParams);
        generator.AppendTokenSequences(sequences);

        // Never batch-decode a zero-length token array — the native tokenizer.decode([])
        // path is crash-prone. Use the incremental stream decoder, decoding one token
        // at a time. Zero-token completions (which s1-mini legitimately produces for
        // pure filler input) simply return an empty string here without touching any
        // empty-decode path.
        using var tokenizerStream = _tokenizer.CreateStream();
        var output = new System.Text.StringBuilder();

        while (!generator.IsDone())
        {
            ct.ThrowIfCancellationRequested();
            generator.GenerateNextToken();

            var tokenText = tokenizerStream.Decode(generator.GetNextTokens()[0]);
            if (!string.IsNullOrEmpty(tokenText))
            {
                output.Append(tokenText);
            }
        }

        return output.ToString();
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        GenerationParameters parameters,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        using var sequences = _tokenizer.Encode(prompt);
        var inputTokenCount = sequences[0].Length;

        using var genParams = new GeneratorParams(_model);
        ApplyParameters(new OnnxGenerationSearchOptions(genParams), parameters, inputTokenCount);

        using var generator = new Generator(_model, genParams);
        generator.AppendTokenSequences(sequences);

        using var tokenizerStream = _tokenizer.CreateStream();

        while (!generator.IsDone())
        {
            ct.ThrowIfCancellationRequested();
            generator.GenerateNextToken();
            ct.ThrowIfCancellationRequested();

            var tokenText = tokenizerStream.Decode(generator.GetNextTokens()[0]);
            if (!string.IsNullOrEmpty(tokenText))
            {
                yield return tokenText;
            }

            await Task.Yield();
        }
    }

    /// <summary>
    /// Maps <see cref="GenerationParameters"/> to ORT-GenAI native search options.
    /// Internal for direct unit testing of the temperature-0 guard through
    /// <see cref="IGenerationSearchOptions"/>.
    /// </summary>
    internal static void ApplyParameters(
        IGenerationSearchOptions searchOptions,
        GenerationParameters parameters,
        int inputTokenCount = 0)
    {
        var effectiveMaxLength = parameters.MaxOutputTokens.HasValue
            ? Math.Min(parameters.MaxLength, inputTokenCount + parameters.MaxOutputTokens.Value)
            : parameters.MaxLength;

        searchOptions.SetSearchOption("max_length", Math.Max(effectiveMaxLength, inputTokenCount + 1));

        // CRITICAL: ORT-GenAI's native runtime crashes with an integer divide-by-zero
        // if SetSearchOption("temperature", ...) is called with a non-positive value,
        // even when do_sample=false. Greedy decoding (Temperature <= 0) must be
        // achieved by omitting the search option entirely rather than passing 0
        // through — do_sample=false is sufficient on its own to select greedy
        // decoding, and s1-mini runs greedy for every call, so this path is hit
        // every time.
        if (parameters.Temperature > 0)
        {
            searchOptions.SetSearchOption("temperature", parameters.Temperature);
        }

        searchOptions.SetSearchOption("top_p", parameters.TopP);

        if (parameters.TopK.HasValue)
        {
            searchOptions.SetSearchOption("top_k", parameters.TopK.Value);
        }

        if (parameters.RepetitionPenalty != 1.0f)
        {
            searchOptions.SetSearchOption("repetition_penalty", parameters.RepetitionPenalty);
        }

        searchOptions.SetSearchOption("do_sample", parameters.Temperature > 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tokenizer.Dispose();
        _model.Dispose();
    }
}

/// <summary>
/// Test seam abstracting the native ORT-GenAI <c>GeneratorParams.SetSearchOption</c>
/// family of calls, so the temperature-0 guard in <see cref="OnnxGenAIRuntime.ApplyParameters"/>
/// can be unit-tested with a recording fake without constructing a real
/// <see cref="Model"/>.
/// </summary>
internal interface IGenerationSearchOptions
{
    void SetSearchOption(string name, int value);
    void SetSearchOption(string name, float value);
    void SetSearchOption(string name, bool value);
}

/// <summary>
/// Default <see cref="IGenerationSearchOptions"/> implementation — delegates
/// straight to the real ORT-GenAI <see cref="GeneratorParams"/>.
/// </summary>
file sealed class OnnxGenerationSearchOptions(GeneratorParams genParams) : IGenerationSearchOptions
{
    public void SetSearchOption(string name, int value) => genParams.SetSearchOption(name, value);
    public void SetSearchOption(string name, float value) => genParams.SetSearchOption(name, value);
    public void SetSearchOption(string name, bool value) => genParams.SetSearchOption(name, value);
}
