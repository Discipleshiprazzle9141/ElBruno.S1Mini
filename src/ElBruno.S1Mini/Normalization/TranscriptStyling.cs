namespace ElBruno.S1Mini.Normalization;

/// <summary>
/// Controls how formal the cleaned transcript output should read.
/// Passed to the s1-mini model via the <c>[Styling: ...]</c> control line.
/// </summary>
public enum TranscriptStyling
{
    /// <summary>
    /// Formal written style. Wire value: <c>formal</c>. Empirically verified to produce a
    /// distinct, more formal register than <see cref="SemiFormal"/> — expands contractions
    /// (e.g. "cannot", "I will").
    /// </summary>
    Formal,

    /// <summary>
    /// Semi-formal written style (default per the model card). Wire value: <c>semi-formal</c>.
    /// </summary>
    SemiFormal,

    /// <summary>
    /// Casual written style. Wire value: <c>casual</c>. Empirically verified to produce a
    /// distinct, more casual register than <see cref="SemiFormal"/> — preserves fillers,
    /// lowercase casing, and contractions with only minimal cleanup.
    /// </summary>
    Casual,
}

internal static class TranscriptStylingExtensions
{
    public static string ToWireValue(this TranscriptStyling styling) => styling switch
    {
        TranscriptStyling.Formal => "formal",
        TranscriptStyling.SemiFormal => "semi-formal",
        TranscriptStyling.Casual => "casual",
        _ => throw new ArgumentOutOfRangeException(nameof(styling), styling, "Unknown transcript styling."),
    };
}
