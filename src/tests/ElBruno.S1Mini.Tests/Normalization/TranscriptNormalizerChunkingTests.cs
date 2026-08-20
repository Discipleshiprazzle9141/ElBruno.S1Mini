using ElBruno.S1Mini.Normalization;
using ElBruno.S1Mini.Tests.TestDoubles;

namespace ElBruno.S1Mini.Tests.Normalization;

/// <summary>
/// Tests for <see cref="TranscriptNormalizer.SplitIntoChunks"/> and
/// <see cref="TranscriptNormalizer.NormalizeChunkedAsync"/> — sentence-boundary
/// chunking for transcripts longer than the model's recommended input size.
/// </summary>
public class TranscriptNormalizerChunkingTests
{
    [Fact]
    public void SplitIntoChunks_ShortInput_YieldsSingleChunk()
    {
        var chunks = TranscriptNormalizer.SplitIntoChunks("This is a short sentence.", maxCharsPerChunk: 3500);

        var chunk = Assert.Single(chunks);
        Assert.Contains("This is a short sentence", chunk);
    }

    [Fact]
    public void SplitIntoChunks_LongInput_NeverExceedsMaxCharsPerChunk()
    {
        var sentence = "This is a moderately long sentence about the quarterly report";
        var transcript = string.Join(" ", Enumerable.Repeat(sentence + ".", 50));

        var chunks = TranscriptNormalizer.SplitIntoChunks(transcript, maxCharsPerChunk: 200);

        Assert.True(chunks.Count > 1, "Expected the long transcript to be split into multiple chunks.");
        Assert.All(chunks, chunk => Assert.True(
            chunk.Length <= 200 + sentence.Length + 2,
            $"Chunk length {chunk.Length} unexpectedly exceeds the limit by more than one sentence."));
    }

    [Fact]
    public void SplitIntoChunks_LongInput_ReassemblesWithoutLosingSentences()
    {
        var sentences = Enumerable.Range(1, 20).Select(i => $"Sentence number {i}").ToList();
        var transcript = string.Join(" ", sentences.Select(s => s + "."));

        var chunks = TranscriptNormalizer.SplitIntoChunks(transcript, maxCharsPerChunk: 60);
        var joined = string.Join(" ", chunks);

        foreach (var sentence in sentences)
        {
            Assert.Contains(sentence, joined);
        }
    }

    [Fact]
    public void SplitIntoChunks_SingleUnbreakableRunLongerThanLimit_DoesNotHangOrThrow()
    {
        var unbreakable = string.Join(" ", Enumerable.Repeat("word", 2000));

        var chunks = TranscriptNormalizer.SplitIntoChunks(unbreakable, maxCharsPerChunk: 50);

        Assert.NotEmpty(chunks);
        Assert.Contains("word", chunks[0]);
    }

    [Fact]
    public void SplitIntoChunks_EmptyInput_ReturnsSingleChunkContainingOriginal()
    {
        var chunks = TranscriptNormalizer.SplitIntoChunks(string.Empty, maxCharsPerChunk: 100);

        var chunk = Assert.Single(chunks);
        Assert.Equal(string.Empty, chunk);
    }

    [Fact]
    public async Task NormalizeChunkedAsync_EmptyOrWhitespaceInput_ReturnsEmptyString_WithoutCallingModel()
    {
        var fakeClient = new FakeChatClient();
        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeChunkedAsync("   ");

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, fakeClient.CallCount);
    }

    [Fact]
    public async Task NormalizeChunkedAsync_ShortInput_CallsModelOnceAndReturnsCleanedText()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueResponse("cleaned short text");
        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeChunkedAsync("A short transcript.");

        Assert.Equal(1, fakeClient.CallCount);
        Assert.Equal("cleaned short text", result);
    }

    [Fact]
    public async Task NormalizeChunkedAsync_LongInput_CallsModelOncePerChunkAndJoinsResults()
    {
        var sentence = "This is a moderately long sentence about the quarterly report";
        var transcript = string.Join(" ", Enumerable.Repeat(sentence + ".", 50));
        var expectedChunkCount = TranscriptNormalizer.SplitIntoChunks(transcript, 200).Count;

        var fakeClient = new FakeChatClient();
        for (var i = 0; i < expectedChunkCount; i++)
        {
            fakeClient.EnqueueResponse($"cleaned chunk {i}");
        }

        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeChunkedAsync(transcript, maxCharsPerChunk: 200);

        Assert.Equal(expectedChunkCount, fakeClient.CallCount);
        for (var i = 0; i < expectedChunkCount; i++)
        {
            Assert.Contains($"cleaned chunk {i}", result);
        }
    }

    [Fact]
    public async Task NormalizeChunkedAsync_ChunksWithEmptyResults_AreOmittedFromJoinedOutput()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueResponse("cleaned first");
        fakeClient.EnqueueResponse(string.Empty);

        using var normalizer = new TranscriptNormalizer(fakeClient);

        var result = await normalizer.NormalizeChunkedAsync(
            "First sentence here. Second sentence here.",
            maxCharsPerChunk: 20);

        Assert.Equal("cleaned first", result);
    }
}
