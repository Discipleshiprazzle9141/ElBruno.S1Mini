using ElBruno.HuggingFace;

namespace ElBruno.S1Mini.Internal;

/// <summary>
/// Downloads <c>elbruno/s1-mini-onnx</c> into a local cache using
/// <c>ElBruno.HuggingFace.Downloader</c>. Resolves the <c>int4/*</c> glob
/// against the HuggingFace API before downloading.
/// </summary>
internal static class ModelResolver
{
    public static async Task<string> ResolveModelPathAsync(
        S1MiniOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.ModelPath))
        {
            return options.ModelPath!;
        }

        var cacheRoot = options.CacheDirectory ?? DefaultCacheDirectory();
        var repoDir = Path.Combine(cacheRoot, SanitizeRepoId(options.RepoId));
        var modelPath = string.IsNullOrWhiteSpace(options.ModelSubPath)
            ? repoDir
            : Path.Combine(repoDir, options.ModelSubPath.Replace('/', Path.DirectorySeparatorChar));

        if (options.EnsureModelDownloaded && !IsCached(modelPath))
        {
            Directory.CreateDirectory(repoDir);

            var resolvedRequired = await ResolveGlobsAsync(
                options.RepoId, options.RequiredFiles, cancellationToken).ConfigureAwait(false);

            using var downloader = new HuggingFaceDownloader();
            var request = new DownloadRequest
            {
                RepoId = options.RepoId,
                LocalDirectory = repoDir,
                RequiredFiles = resolvedRequired,
                Progress = options.DownloadProgress
            };

            await downloader.DownloadFilesAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return modelPath;
    }

    private static bool IsCached(string modelPath)
    {
        if (!Directory.Exists(modelPath))
            return false;

        if (!File.Exists(Path.Combine(modelPath, "genai_config.json")))
            return false;

        var onnxFiles = Directory.EnumerateFiles(modelPath, "*.onnx", SearchOption.AllDirectories).ToArray();
        if (onnxFiles.Length == 0)
            return false;

        var dataFiles = Directory.EnumerateFiles(modelPath, "*.onnx.data", SearchOption.AllDirectories).ToArray();
        if (dataFiles.Length > 0)
            return true;

        const long minimumStandaloneOnnxBytes = 100L * 1024L * 1024L;
        return onnxFiles.All(path => new FileInfo(path).Length >= minimumStandaloneOnnxBytes);
    }

    private static async Task<string[]> ResolveGlobsAsync(
        string repoId, string[] patterns, CancellationToken ct)
    {
        var hasGlobs = Array.Exists(patterns, p => p.Contains('*'));
        if (!hasGlobs)
            return patterns;

        var allFiles = await ListRepoFilesAsync(repoId, ct).ConfigureAwait(false);
        var resolved = new List<string>();

        foreach (var pattern in patterns)
        {
            if (!pattern.Contains('*'))
            {
                resolved.Add(pattern);
                continue;
            }

            if (pattern == "*")
            {
                resolved.AddRange(allFiles.Where(f => !f.StartsWith('.') && f != ".gitattributes"));
            }
            else if (pattern.EndsWith("/*", StringComparison.Ordinal))
            {
                var prefix = pattern[..^1];
                resolved.AddRange(allFiles.Where(f => f.StartsWith(prefix, StringComparison.Ordinal)));
            }
            else
            {
                resolved.Add(pattern);
            }
        }

        if (resolved.Count == 0)
        {
            throw new InvalidOperationException(
                $"No files matched the required patterns [{string.Join(", ", patterns)}] in repo '{repoId}'.");
        }

        return [.. resolved];
    }

    private static async Task<List<string>> ListRepoFilesAsync(string repoId, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ElBruno.S1Mini/0.1");

        var url = $"https://huggingface.co/api/models/{repoId}/tree/main?recursive=true";
        var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var files = new List<string>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("type", out var type) &&
                type.GetString() == "file" &&
                element.TryGetProperty("path", out var path))
            {
                var p = path.GetString();
                if (!string.IsNullOrWhiteSpace(p)) files.Add(p);
            }
        }

        return files;
    }

    private static string DefaultCacheDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElBruno", "S1Mini", "models");

    private static string SanitizeRepoId(string repoId)
        => repoId.Replace('/', '_').Replace('\\', '_');
}
