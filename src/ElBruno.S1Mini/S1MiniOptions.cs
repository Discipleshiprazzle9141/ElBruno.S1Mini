using ElBruno.HuggingFace;

namespace ElBruno.S1Mini;

/// <summary>
/// Options for <see cref="S1MiniClient"/>.
/// </summary>
public sealed class S1MiniOptions
{
    /// <summary>
    /// HuggingFace repository containing the ONNX conversion.
    /// Default: <c>elbruno/s1-mini-onnx</c>.
    /// </summary>
    public string RepoId { get; set; } = "elbruno/s1-mini-onnx";

    /// <summary>
    /// Subfolder within the repo that contains the ONNX model files.
    /// Default: <c>int4</c> — the only variant known to work on CPU with
    /// <c>onnxruntime-genai</c> 0.15.1. FP16 is published but currently
    /// non-functional on CPU due to an upstream ORT GQA <c>repeat_kv</c>
    /// Reshape shape-mismatch bug — do not switch to <c>fp16</c> without
    /// re-verifying against the runtime.
    /// </summary>
    public string ModelSubPath { get; set; } = "int4";

    /// <summary>
    /// Glob patterns for required files. Default: <c>int4/*</c>.
    /// </summary>
    public string[] RequiredFiles { get; set; } = ["int4/*"];

    /// <summary>
    /// Local cache directory. Defaults to
    /// <c>%LOCALAPPDATA%/ElBruno/S1Mini/models</c> (or the platform equivalent).
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Explicit path to an already-downloaded model directory. When set, no
    /// download is attempted regardless of <see cref="EnsureModelDownloaded"/>.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Whether to download the model on first use if it is not already cached.
    /// Default: <c>true</c>.
    /// </summary>
    public bool EnsureModelDownloaded { get; set; } = true;

    /// <summary>
    /// Optional progress reporter for the initial model download.
    /// </summary>
    public IProgress<DownloadProgress>? DownloadProgress { get; set; }
}
