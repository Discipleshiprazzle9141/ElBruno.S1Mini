using Microsoft.Extensions.AI;

namespace ElBruno.S1Mini.Tests.TestDoubles;

/// <summary>
/// A minimal, fully offline <see cref="IChatClient"/> test double used to verify
/// callers (e.g. <see cref="Normalization.TranscriptNormalizer"/>) without requiring
/// a real model. Records every call so tests can assert on what was sent.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _responses = new();

    public int CallCount { get; private set; }
    public bool Disposed { get; private set; }
    public IList<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }
    public string NextResponseText { get; set; } = string.Empty;

    public void EnqueueResponse(string text) => _responses.Enqueue(text);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastMessages = messages as IList<ChatMessage> ?? messages.ToList();
        LastOptions = options;
        LastCancellationToken = cancellationToken;

        var text = _responses.Count > 0 ? _responses.Dequeue() : NextResponseText;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("FakeChatClient does not support streaming.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => Disposed = true;
}
