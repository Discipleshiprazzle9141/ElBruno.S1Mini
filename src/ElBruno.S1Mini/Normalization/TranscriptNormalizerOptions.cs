namespace ElBruno.S1Mini.Normalization;

/// <summary>
/// Options controlling how <see cref="TranscriptNormalizer"/> cleans a raw
/// speech-to-text transcript.
/// </summary>
public sealed class TranscriptNormalizerOptions
{
    /// <summary>How formal the cleaned output should read. Default: <see cref="TranscriptStyling.SemiFormal"/>.</summary>
    public TranscriptStyling Styling { get; set; } = TranscriptStyling.SemiFormal;

    /// <summary>The output shape (prose vs. lists). Default: <see cref="TranscriptStructure.Prose"/>.</summary>
    public TranscriptStructure Structure { get; set; } = TranscriptStructure.Prose;

    /// <summary>The intended use of the output. Default: <see cref="TranscriptContext.General"/>.</summary>
    public TranscriptContext Context { get; set; } = TranscriptContext.General;

    /// <summary>
    /// Maximum number of tokens to generate. Default: 1024, matching the model
    /// card's recommended <c>max_new_tokens</c> for greedy decoding.
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// Overrides the system prompt sent to the model. Defaults to the verbatim
    /// model-card system prompt (<see cref="TranscriptNormalizer.DefaultSystemPrompt"/>).
    /// Only override this if you know what you are doing — s1-mini was fine-tuned
    /// against this exact instruction.
    /// </summary>
    public string? SystemPrompt { get; set; }
}
