using ElBruno.S1Mini.Normalization;

namespace ElBruno.S1Mini.Tests.Normalization;

/// <summary>
/// Tests for <see cref="TranscriptNormalizer.BuildPrompt"/> — the s1-mini
/// control-line + raw transcript prompt construction.
/// </summary>
public class TranscriptNormalizerBuildPromptTests
{
    [Fact]
    public void BuildPrompt_WithDefaultOptions_ProducesExpectedControlLineAndTranscript()
    {
        var options = new TranscriptNormalizerOptions();

        var prompt = TranscriptNormalizer.BuildPrompt("hello world", options);

        Assert.Equal("[Styling: semi-formal] [Structure: prose] [Context: general]\nhello world", prompt);
    }

    [Theory]
    [InlineData(TranscriptStyling.Formal, "formal")]
    [InlineData(TranscriptStyling.SemiFormal, "semi-formal")]
    [InlineData(TranscriptStyling.Casual, "casual")]
    public void BuildPrompt_StylingValues_MapToExpectedWireValue(TranscriptStyling styling, string expectedWireValue)
    {
        var options = new TranscriptNormalizerOptions { Styling = styling };

        var prompt = TranscriptNormalizer.BuildPrompt("text", options);

        Assert.Contains($"[Styling: {expectedWireValue}]", prompt);
    }

    [Theory]
    [InlineData(TranscriptStructure.Prose, "prose")]
    [InlineData(TranscriptStructure.Lists, "lists")]
    public void BuildPrompt_StructureValues_MapToExpectedWireValue(TranscriptStructure structure, string expectedWireValue)
    {
        var options = new TranscriptNormalizerOptions { Structure = structure };

        var prompt = TranscriptNormalizer.BuildPrompt("text", options);

        Assert.Contains($"[Structure: {expectedWireValue}]", prompt);
    }

    [Theory]
    [InlineData(TranscriptContext.General, "general")]
    [InlineData(TranscriptContext.Email, "email")]
    [InlineData(TranscriptContext.Message, "message")]
    [InlineData(TranscriptContext.Notes, "notes")]
    public void BuildPrompt_ContextValues_MapToExpectedWireValue(TranscriptContext context, string expectedWireValue)
    {
        var options = new TranscriptNormalizerOptions { Context = context };

        var prompt = TranscriptNormalizer.BuildPrompt("text", options);

        Assert.Contains($"[Context: {expectedWireValue}]", prompt);
    }

    [Fact]
    public void BuildPrompt_Structure_Lists_And_Context_Email_RenderTogetherCorrectly()
    {
        var options = new TranscriptNormalizerOptions
        {
            Structure = TranscriptStructure.Lists,
            Context = TranscriptContext.Email,
        };

        var prompt = TranscriptNormalizer.BuildPrompt("some transcript", options);

        Assert.Equal("[Styling: semi-formal] [Structure: lists] [Context: email]\nsome transcript", prompt);
    }

    [Fact]
    public void BuildPrompt_TranscriptIsPassedThroughVerbatim_NotTrimmedOrRecased()
    {
        const string transcript = "  So UM i need  to LIKE send the report.  ";
        var options = new TranscriptNormalizerOptions();

        var prompt = TranscriptNormalizer.BuildPrompt(transcript, options);

        Assert.EndsWith("\n" + transcript, prompt);
    }

    [Fact]
    public void BuildPrompt_ControlLineIsFollowedByNewlineThenTranscript()
    {
        var options = new TranscriptNormalizerOptions();

        var prompt = TranscriptNormalizer.BuildPrompt("abc", options);
        var lines = prompt.Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal("[Styling: semi-formal] [Structure: prose] [Context: general]", lines[0]);
        Assert.Equal("abc", lines[1]);
    }

    [Fact]
    public void BuildPrompt_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TranscriptNormalizer.BuildPrompt("abc", null!));
    }
}
