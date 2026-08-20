using ElBruno.S1Mini.Normalization;
using ElBruno.S1Mini.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace ElBruno.S1Mini.Tests.Normalization;

/// <summary>
/// Tests for <see cref="TranscriptNormalizer.NormalizeAsync"/> — request shape,
/// short-circuiting on empty input, and response handling — all against a fake
/// <see cref="IChatClient"/> (no model, no network).
/// </summary>
public class TranscriptNormalizerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n  \r\n")]
    public async Task NormalizeAsync_EmptyOrWhitespaceInput_ReturnsEmptyString_WithoutCallingModel(string transcript)
    {
        var fakeClient = new FakeChatClient();
        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeAsync(transcript);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, fakeClient.CallCount);
    }

    [Fact]
    public async Task NormalizeAsync_NullInput_ReturnsEmptyString_WithoutCallingModel()
    {
        var fakeClient = new FakeChatClient();
        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeAsync(null!);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, fakeClient.CallCount);
    }

    [Fact]
    public async Task NormalizeAsync_SendsDefaultSystemPromptVerbatim()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        await normalizer.NormalizeAsync("raw transcript");

        Assert.NotNull(fakeClient.LastMessages);
        var systemMessage = Assert.Single(fakeClient.LastMessages!, m => m.Role == ChatRole.System);
        Assert.Equal(TranscriptNormalizer.DefaultSystemPrompt, systemMessage.Text);
    }

    [Fact]
    public async Task NormalizeAsync_CustomSystemPromptOverride_IsHonored()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);
        var options = new TranscriptNormalizerOptions { SystemPrompt = "custom system prompt" };

        await normalizer.NormalizeAsync("raw transcript", options);

        var systemMessage = Assert.Single(fakeClient.LastMessages!, m => m.Role == ChatRole.System);
        Assert.Equal("custom system prompt", systemMessage.Text);
    }

    [Fact]
    public async Task NormalizeAsync_UsesGreedyDecoding_TemperatureZero()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        await normalizer.NormalizeAsync("raw transcript");

        Assert.NotNull(fakeClient.LastOptions);
        Assert.Equal(0f, fakeClient.LastOptions!.Temperature);
    }

    [Fact]
    public async Task NormalizeAsync_TemperatureContractIsExactlyZero_NotAPositiveEpsilon()
    {
        // Regression guard: a well-meaning "fix" for the ORT-GenAI native
        // divide-by-zero crash might be tempted to swap Temperature=0f for a tiny
        // positive value like 0.01f. That would silently flip do_sample to true
        // downstream and defeat s1-mini's required greedy decoding. Pin the
        // contract explicitly: Temperature must remain exactly 0f.
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        await normalizer.NormalizeAsync("raw transcript");

        Assert.NotNull(fakeClient.LastOptions?.Temperature);
        Assert.False(fakeClient.LastOptions!.Temperature > 0f, "Temperature must not be positive — that would defeat greedy decoding.");
    }

    [Fact]
    public async Task NormalizeAsync_DefaultMaxTokens_Is1024()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        await normalizer.NormalizeAsync("raw transcript");

        Assert.Equal(1024, fakeClient.LastOptions!.MaxOutputTokens);
    }

    [Fact]
    public async Task NormalizeAsync_CustomMaxTokens_IsPropagatedToChatOptions()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);
        var options = new TranscriptNormalizerOptions { MaxTokens = 256 };

        await normalizer.NormalizeAsync("raw transcript", options);

        Assert.Equal(256, fakeClient.LastOptions!.MaxOutputTokens);
    }

    [Fact]
    public async Task NormalizeAsync_SendsExactlyTwoMessages_SystemThenUser()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        await normalizer.NormalizeAsync("raw transcript");

        Assert.Equal(2, fakeClient.LastMessages!.Count);
        Assert.Equal(ChatRole.System, fakeClient.LastMessages![0].Role);
        Assert.Equal(ChatRole.User, fakeClient.LastMessages![1].Role);
    }

    [Fact]
    public async Task NormalizeAsync_UserMessage_ContainsControlLineAndTranscript()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        await normalizer.NormalizeAsync("raw transcript");

        var userMessage = fakeClient.LastMessages![1];
        Assert.Equal(
            TranscriptNormalizer.BuildPrompt("raw transcript", new TranscriptNormalizerOptions()),
            userMessage.Text);
    }

    [Fact]
    public async Task NormalizeAsync_ReturnsResponseTextTrimmed()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "  cleaned transcript  " };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeAsync("raw transcript");

        Assert.Equal("cleaned transcript", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task NormalizeAsync_PureFillerInput_ModelReturnsEmptyResponse_YieldsEmptyString(string modelResponse)
    {
        // Documented, first-class scenario: for pure filler/noise input, s1-mini
        // emits EOS immediately and generates zero tokens. Expected model behavior
        // that must surface as string.Empty.
        var fakeClient = new FakeChatClient { NextResponseText = modelResponse };
        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeAsync("um uh so like yeah");

        Assert.Equal(string.Empty, result);
        Assert.Equal(1, fakeClient.CallCount);
    }

    [Fact]
    public async Task NormalizeAsync_PropagatesCancellationTokenToChatClient()
    {
        var fakeClient = new FakeChatClient { NextResponseText = "cleaned" };
        using var normalizer = new TranscriptNormalizer(fakeClient);
        using var cts = new CancellationTokenSource();

        await normalizer.NormalizeAsync("raw transcript", cancellationToken: cts.Token);

        Assert.Equal(cts.Token, fakeClient.LastCancellationToken);
    }

    [Fact]
    public void Dispose_PubliclyConstructedNormalizer_DoesNotDisposeInjectedChatClient()
    {
        var fakeClient = new FakeChatClient();
        var normalizer = new TranscriptNormalizer(fakeClient);

        normalizer.Dispose();

        Assert.False(fakeClient.Disposed);
    }

    [Fact]
    public async Task Dispose_ThenNormalizeAsync_ThrowsObjectDisposedException()
    {
        var fakeClient = new FakeChatClient();
        var normalizer = new TranscriptNormalizer(fakeClient);
        normalizer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => normalizer.NormalizeAsync("hello"));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var fakeClient = new FakeChatClient();
        var normalizer = new TranscriptNormalizer(fakeClient);

        normalizer.Dispose();
        var exception = Record.Exception(() => normalizer.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_NullChatClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TranscriptNormalizer(null!));
    }

    [Fact]
    public void Dispose_InternalOwnsChatClientTrue_DisposesChatClient()
    {
        var fakeClient = new FakeChatClient();
        var normalizer = new TranscriptNormalizer(fakeClient, ownsChatClient: true);

        normalizer.Dispose();

        Assert.True(fakeClient.Disposed);
    }

    [Fact]
    public void Dispose_InternalOwnsChatClientFalse_DoesNotDisposeChatClient()
    {
        var fakeClient = new FakeChatClient();
        var normalizer = new TranscriptNormalizer(fakeClient, ownsChatClient: false);

        normalizer.Dispose();

        Assert.False(fakeClient.Disposed);
    }
}
