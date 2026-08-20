using ElBruno.S1Mini.Internal;

namespace ElBruno.S1Mini.Tests.Internal;

/// <summary>
/// Direct tests of <see cref="OnnxGenAIRuntime.ApplyParameters"/> — in particular
/// the ORT-GenAI native divide-by-zero guard around the <c>"temperature"</c>
/// search option — via the <see cref="IGenerationSearchOptions"/> seam, so no
/// real ONNX <c>Model</c> is required.
/// </summary>
public class OnnxGenAIRuntimeTemperatureTests
{
    private sealed class RecordingSearchOptions : IGenerationSearchOptions
    {
        public Dictionary<string, int> Ints { get; } = new();
        public Dictionary<string, float> Floats { get; } = new();
        public Dictionary<string, bool> Bools { get; } = new();

        public void SetSearchOption(string name, int value) => Ints[name] = value;
        public void SetSearchOption(string name, float value) => Floats[name] = value;
        public void SetSearchOption(string name, bool value) => Bools[name] = value;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(-0.5f)]
    public void ApplyParameters_TemperatureLeqZero_NeverSetsTemperatureOption_AndSetsDoSampleFalse(float temperature)
    {
        var recorder = new RecordingSearchOptions();
        var parameters = new GenerationParameters(Temperature: temperature);

        OnnxGenAIRuntime.ApplyParameters(recorder, parameters, inputTokenCount: 10);

        Assert.False(recorder.Floats.ContainsKey("temperature"),
            "Temperature must NOT be set for non-positive values — the native runtime crashes with divide-by-zero.");
        Assert.False(recorder.Bools["do_sample"]);
    }

    [Fact]
    public void ApplyParameters_TemperaturePositive_SetsTemperatureOption_AndSetsDoSampleTrue()
    {
        var recorder = new RecordingSearchOptions();
        var parameters = new GenerationParameters(Temperature: 0.7f);

        OnnxGenAIRuntime.ApplyParameters(recorder, parameters, inputTokenCount: 10);

        Assert.Equal(0.7f, recorder.Floats["temperature"]);
        Assert.True(recorder.Bools["do_sample"]);
    }

    [Fact]
    public void ApplyParameters_MaxOutputTokens_MapsToInputPlusOutput()
    {
        var recorder = new RecordingSearchOptions();
        var parameters = new GenerationParameters(MaxLength: 4096, MaxOutputTokens: 256);

        OnnxGenAIRuntime.ApplyParameters(recorder, parameters, inputTokenCount: 50);

        Assert.Equal(306, recorder.Ints["max_length"]);
    }

    [Fact]
    public void ApplyParameters_MaxOutputTokensExceedsMaxLength_Clamps()
    {
        var recorder = new RecordingSearchOptions();
        var parameters = new GenerationParameters(MaxLength: 200, MaxOutputTokens: 4096);

        OnnxGenAIRuntime.ApplyParameters(recorder, parameters, inputTokenCount: 50);

        Assert.Equal(200, recorder.Ints["max_length"]);
    }

    [Fact]
    public void ApplyParameters_NoMaxOutputTokens_UsesMaxLengthAsIs()
    {
        var recorder = new RecordingSearchOptions();
        var parameters = new GenerationParameters(MaxLength: 2048, MaxOutputTokens: null);

        OnnxGenAIRuntime.ApplyParameters(recorder, parameters, inputTokenCount: 100);

        Assert.Equal(2048, recorder.Ints["max_length"]);
    }

    [Fact]
    public void ApplyParameters_TopP_IsAlwaysSet()
    {
        var recorder = new RecordingSearchOptions();
        OnnxGenAIRuntime.ApplyParameters(recorder, new GenerationParameters(TopP: 0.85f));

        Assert.Equal(0.85f, recorder.Floats["top_p"]);
    }

    [Fact]
    public void ApplyParameters_TopK_OnlyWhenNonNull()
    {
        var withRecorder = new RecordingSearchOptions();
        OnnxGenAIRuntime.ApplyParameters(withRecorder, new GenerationParameters(TopK: 40));
        Assert.Equal(40, withRecorder.Ints["top_k"]);

        var withoutRecorder = new RecordingSearchOptions();
        OnnxGenAIRuntime.ApplyParameters(withoutRecorder, new GenerationParameters(TopK: null));
        Assert.False(withoutRecorder.Ints.ContainsKey("top_k"));
    }

    [Fact]
    public void ApplyParameters_RepetitionPenalty_OnlyWhenNotOne()
    {
        var withRecorder = new RecordingSearchOptions();
        OnnxGenAIRuntime.ApplyParameters(withRecorder, new GenerationParameters(RepetitionPenalty: 1.1f));
        Assert.Equal(1.1f, withRecorder.Floats["repetition_penalty"]);

        var noneRecorder = new RecordingSearchOptions();
        OnnxGenAIRuntime.ApplyParameters(noneRecorder, new GenerationParameters(RepetitionPenalty: 1.0f));
        Assert.False(noneRecorder.Floats.ContainsKey("repetition_penalty"));
    }
}
