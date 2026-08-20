using ElBruno.S1Mini;
using ElBruno.S1Mini.Normalization;

namespace ElBruno.S1Mini.Tests.Normalization;

/// <summary>
/// Opt-in real-model regression tests for published before/after claims in
/// <c>docs/blogs/26-08-20-intro.md</c>.
/// These tests are skipped unless <c>S1MINI_RUN_MODEL_TESTS=1</c> because they
/// load the local ~390 MB INT4 ONNX model. They exist so dependency, option, or
/// prompt-construction drift cannot silently invalidate the blog's quoted output.
/// </summary>
public sealed class TranscriptNormalizerModelTests : IClassFixture<S1MiniModelFixture>
{
    private readonly S1MiniModelFixture _fixture;

    public TranscriptNormalizerModelTests(S1MiniModelFixture fixture)
    {
        _fixture = fixture;
    }

    [S1MiniModelFact]
    public Task NormalizeAsync_RepeatedChangeClaim_ReproducesPublishedBlogOutput() => VerifyPublishedBlogClaimAsync(
        "you don't have any any any any change at all?",
        "You don't have any change at all.");

    [S1MiniModelFact]
    public Task NormalizeAsync_ReportDateCorrectionClaim_ReproducesPublishedBlogOutput() => VerifyPublishedBlogClaimAsync(
        "so um i need to like send the the report by uh friday no wait make that thursday",
        "So I need to send the report by Thursday.");

    [S1MiniModelFact]
    public Task NormalizeAsync_RepeatedWordsClaim_ReproducesPublishedBlogOutput() => VerifyPublishedBlogClaimAsync(
        "and then we we we need to to look at the the numbers",
        "And then we need to look at the numbers.");

    [S1MiniModelFact]
    public Task NormalizeAsync_OptionCorrectionClaim_ReproducesPublishedBlogOutput() => VerifyPublishedBlogClaimAsync(
        "i think we should uh go with with option b i mean option c",
        "I think we should go with option C.");

    [S1MiniModelFact]
    public Task NormalizeAsync_CurrencyClaim_ReproducesPublishedBlogOutput() => VerifyPublishedBlogClaimAsync(
        "the the total was like twenty five dollars and uh fifty cents",
        "The total was like $25.50.");

    [S1MiniModelFact]
    public Task NormalizeAsync_ColloquialLikeClaim_ReproducesPublishedBlogOutput() => VerifyPublishedBlogClaimAsync(
        "um so yeah like you know basically",
        "So yeah, basically");

    [S1MiniModelFact]
    public Task NormalizeAsync_TimeAndDateClaim_ReproducesPublishedBlogOutput() => VerifyPublishedBlogClaimAsync(
        "lets meet at uh three thirty on on tuesday the the tenth",
        "Let's meet at 3:30 on Tuesday the 10th.");

    private async Task VerifyPublishedBlogClaimAsync(string rawInput, string expectedOutput)
    {
        var actual = await _fixture.Normalizer.NormalizeAsync(rawInput);

        Assert.True(
            string.Equals(expectedOutput, actual, StringComparison.Ordinal),
            $"Real s1-mini output changed for a published claim in docs/blogs/26-08-20-intro.md. If this drift is intentional, review/update that post.\nRaw input: {rawInput}\nExpected: {expectedOutput}\nActual:   {actual}");
    }
}

public sealed class S1MiniModelFixture : IAsyncLifetime
{
    private TranscriptNormalizer? _normalizer;

    public TranscriptNormalizer Normalizer => _normalizer
        ?? throw new InvalidOperationException("The s1-mini model fixture was not initialized. Set S1MINI_RUN_MODEL_TESTS=1 to run these tests.");

    public async Task InitializeAsync()
    {
        if (!S1MiniModelFactAttribute.IsEnabled)
        {
            return;
        }

        var modelPath = Environment.GetEnvironmentVariable("S1MINI_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            modelPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElBruno",
                "S1Mini",
                "models",
                "elbruno_s1-mini-onnx",
                "int4");
        }

        _normalizer = await TranscriptNormalizer.CreateAsync(new S1MiniOptions
        {
            ModelPath = modelPath,
            EnsureModelDownloaded = false,
        });
    }

    public Task DisposeAsync()
    {
        _normalizer?.Dispose();
        return Task.CompletedTask;
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class S1MiniModelFactAttribute : FactAttribute
{
    public const string GateEnvironmentVariable = "S1MINI_RUN_MODEL_TESTS";

    public S1MiniModelFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = $"Set {GateEnvironmentVariable}=1 to run real s1-mini model regression tests.";
        }
    }

    public static bool IsEnabled => string.Equals(
        Environment.GetEnvironmentVariable(GateEnvironmentVariable),
        "1",
        StringComparison.Ordinal);
}
