namespace ElBruno.S1Mini.Normalization;

/// <summary>
/// Describes the intended use of the cleaned transcript, which influences
/// conventions such as greeting/sign-off formatting.
/// Passed to the s1-mini model via the <c>[Context: ...]</c> control line.
/// </summary>
public enum TranscriptContext
{
    /// <summary>General-purpose context (default per the model card). Wire value: <c>general</c>.</summary>
    General,

    /// <summary>
    /// Email context, as demonstrated on the model card — output may contain
    /// blank-line-separated greeting/body/sign-off. Wire value: <c>email</c>.
    /// </summary>
    Email,

    /// <summary>
    /// Chat/instant message context. Wire value: <c>message</c>.
    /// <b>Empirically verified to behave identically to <see cref="General"/></b> —
    /// the model accepts this token without error but does not apply any distinct
    /// formatting for it. Kept for API completeness / forward-compatibility with a
    /// possible future model update, not because it currently changes output.
    /// </summary>
    Message,

    /// <summary>
    /// Free-form notes context. Wire value: <c>notes</c>.
    /// <b>Empirically verified to behave identically to <see cref="General"/></b> —
    /// the model accepts this token without error but does not apply any distinct
    /// formatting for it. Kept for API completeness / forward-compatibility with a
    /// possible future model update, not because it currently changes output.
    /// </summary>
    Notes,
}

internal static class TranscriptContextExtensions
{
    public static string ToWireValue(this TranscriptContext context) => context switch
    {
        TranscriptContext.General => "general",
        TranscriptContext.Email => "email",
        TranscriptContext.Message => "message",
        TranscriptContext.Notes => "notes",
        _ => throw new ArgumentOutOfRangeException(nameof(context), context, "Unknown transcript context."),
    };
}
