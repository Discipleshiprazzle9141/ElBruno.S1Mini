namespace ElBruno.S1Mini.Normalization;

/// <summary>
/// Controls the output shape of the cleaned transcript.
/// Passed to the s1-mini model via the <c>[Structure: ...]</c> control line.
/// </summary>
public enum TranscriptStructure
{
    /// <summary>Continuous prose (default per the model card). Wire value: <c>prose</c>.</summary>
    Prose,

    /// <summary>
    /// Wire value: <c>lists</c>. The model card documents this as producing Markdown
    /// bullet-point list output, but empirical testing against the real INT4 model did
    /// not reproduce that: output remained prose (reworded, not bulleted/numbered) in
    /// every test run. Kept as a documented model-card value; do not assume it reliably
    /// yields literal list formatting.
    /// </summary>
    Lists,
}

internal static class TranscriptStructureExtensions
{
    public static string ToWireValue(this TranscriptStructure structure) => structure switch
    {
        TranscriptStructure.Prose => "prose",
        TranscriptStructure.Lists => "lists",
        _ => throw new ArgumentOutOfRangeException(nameof(structure), structure, "Unknown transcript structure."),
    };
}
