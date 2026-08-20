# Getting Started

## Install

```bash
dotnet add package ElBruno.S1Mini
```

## Minimal example

```csharp
using ElBruno.S1Mini.Normalization;

using var normalizer = await TranscriptNormalizer.CreateAsync();

var cleaned = await normalizer.NormalizeAsync(
    "so um i need to like send the the report by uh friday no wait make that thursday");

Console.WriteLine(cleaned);
```

On first run, the INT4 ONNX model (~500 MB) is downloaded from `elbruno/s1-mini-onnx` on HuggingFace to `%LOCALAPPDATA%/ElBruno/S1Mini/models`. Subsequent runs load from cache.

## Configuration

```csharp
var options = new S1MiniOptions
{
    CacheDirectory = @"C:\models",       // override the cache root
    ModelPath = @"C:\models\s1-mini\int4", // skip download entirely, use a local dir
    EnsureModelDownloaded = true,        // set false to fail-fast if not cached
};

using var normalizer = await TranscriptNormalizer.CreateAsync(options);
```

## Per-call options

```csharp
using ElBruno.S1Mini.Normalization;

var cleaned = await normalizer.NormalizeAsync(
    rawTranscript,
    new TranscriptNormalizerOptions
    {
        Styling = TranscriptStyling.Formal,
        Structure = TranscriptStructure.Prose,
        Context = TranscriptContext.Email,
        MaxTokens = 1024,
    });
```

See [transcript-normalization.md](transcript-normalization.md) for the empirically verified behavior of each setting.

## Dependency injection

```csharp
using ElBruno.S1Mini;

builder.Services.AddTranscriptNormalizer(options =>
{
    options.CacheDirectory = @"C:\models";
});
```

Then inject `TranscriptNormalizer` into your consumers. Note it is deliberately **not** registered as `IChatClient` — s1-mini is not a general chat model.
