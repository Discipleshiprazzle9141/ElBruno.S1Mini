# ElBruno.S1Mini

[![NuGet](https://img.shields.io/nuget/v/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini)
[![CI Build](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/build.yml/badge.svg)](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/build.yml)
[![Publish to NuGet](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/publish.yml/badge.svg)](https://github.com/elbruno/ElBruno.S1Mini/actions/workflows/publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)
[![HuggingFace](https://img.shields.io/badge/🤗_HuggingFace-ONNX_Model-orange?style=flat-square)](https://huggingface.co/elbruno/s1-mini-onnx)
[![.NET](https://img.shields.io/badge/.NET-8.0%20|%2010.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![GitHub stars](https://img.shields.io/github/stars/elbruno/ElBruno.S1Mini?style=social)](https://github.com/elbruno/ElBruno.S1Mini)

## Local ASR transcript normalizer for .NET 🧹

Clean raw speech-to-text transcripts into well-formed written text in .NET. Powered by [`superwhisper/s1-mini`](https://huggingface.co/superwhisper/s1-mini) (a 0.6B Qwen3 fine-tune) running locally via ONNX Runtime GenAI. Auto-downloads the INT4 ONNX model from HuggingFace.

> ⚠️ **s1-mini is not a chat model.** It performs exactly one task — normalizing an ASR transcript. Give it anything else and you get unpredictable output.

## Packages

| Package | NuGet | Downloads | Description |
|---------|-------|-----------|-------------|
| `ElBruno.S1Mini` | [![NuGet](https://img.shields.io/nuget/v/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini) | [![NuGet Downloads](https://img.shields.io/nuget/dt/ElBruno.S1Mini.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/ElBruno.S1Mini) | Self-contained transcript normalizer with `S1MiniClient` (`IChatClient`) and `TranscriptNormalizer`. |

## Features

- 🧹 **Filler removal + punctuation + capitalization** in one call
- 🔁 **Self-correction resolution** — `"nine am no sorry ten thirty"` becomes `10:30`
- 🔢 **Written form for spoken numbers, dates, times, currency, emails**
- 🎚️ **Control-line settings** — `Styling` (formal / semi-formal / casual) and `Context` (general / email) with empirically verified behavior
- 📦 **Auto-download** — INT4 ONNX model fetched from HuggingFace on first use
- 🧵 **`IChatClient` compatible** — `S1MiniClient` plugs into `Microsoft.Extensions.AI`
- 💉 **DI-friendly** — `AddTranscriptNormalizer()` for ASP.NET Core
- 🪁 **Chunking helper** — `NormalizeChunkedAsync` for transcripts longer than the ~1,000-token recommended input
- 🛑 **Empty-in / empty-out** — pure filler returns `string.Empty`, as the model card documents
- 🦺 **Temperature-0 safe** — the ORT-GenAI native divide-by-zero trap is guarded at the runtime layer so callers can always use greedy decoding

## Installation

```bash
dotnet add package ElBruno.S1Mini
```

## Quick Start

```csharp
using ElBruno.S1Mini.Normalization;

// Downloads elbruno/s1-mini-onnx (int4) on first run.
using var normalizer = await TranscriptNormalizer.CreateAsync();

var cleaned = await normalizer.NormalizeAsync(
    "so um i need to like send the the report by uh friday no wait make that thursday");

Console.WriteLine(cleaned);
// So I need to send the report by Thursday.
```

## Control-line options

```csharp
using ElBruno.S1Mini.Normalization;

var cleaned = await normalizer.NormalizeAsync(
    transcript,
    new TranscriptNormalizerOptions
    {
        Styling = TranscriptStyling.Formal,
        Context = TranscriptContext.Email,
    });
```

Empirically verified against the real INT4 model:

- `Styling`: `SemiFormal` (default), `Formal`, `Casual` — all three produce distinct output.
- `Structure`: `Prose` (default), `Lists` — **caveat:** `Lists` did **not** reliably produce Markdown bullets in testing; output stayed prose. Kept as a model-card value.
- `Context`: `General` (default), `Email` — distinct. `Message` and `Notes` are accepted but empirically behave **identically to `General`**; kept for API completeness.

See [docs/transcript-normalization.md](docs/transcript-normalization.md) for the full behavior table and before/after examples.

## Dependency Injection

```csharp
using ElBruno.S1Mini;

builder.Services.AddTranscriptNormalizer(options =>
{
    options.CacheDirectory = @"C:\models";
});
```

`TranscriptNormalizer` is registered as its own service type — not as `IChatClient`. s1-mini is not a chat model, and exposing it as one would mislead consumers expecting chat semantics.

## Compose with any `IChatClient`

`TranscriptNormalizer` is a control-line + prompt-builder layer over any `IChatClient`. Supply your own client (as long as it is really pointed at s1-mini):

```csharp
using ElBruno.S1Mini;
using ElBruno.S1Mini.Normalization;

using var chatClient = await S1MiniClient.CreateAsync();
using var normalizer = new TranscriptNormalizer(chatClient);
```

`S1MiniClient` also implements `IChatClient` directly, so you can plug it into any `Microsoft.Extensions.AI` pipeline — with the caveat that it only handles the exact prompt shape s1-mini was fine-tuned on.

## Chunking long transcripts

```csharp
var cleaned = await normalizer.NormalizeChunkedAsync(longTranscript, maxCharsPerChunk: 3500);
```

Each chunk is normalized statelessly at sentence boundaries; for tighter control on transcripts with context spanning boundaries, chunk manually at a natural pause instead.

## FP16 is currently broken on CPU

`elbruno/s1-mini-onnx` also has an `fp16/` subfolder, but that variant fails at inference on CPU with `onnxruntime-genai` 0.15.1 (upstream ORT GQA `repeat_kv` Reshape shape-mismatch bug). **Use INT4 (the default).** This library will not switch to FP16 automatically.

## Model license

`superwhisper/s1-mini` is Apache-2.0 with a naming clause. The converted ONNX artifacts (`elbruno/s1-mini-onnx`) are an **explicitly unofficial, unaffiliated, non-endorsed derivative**. `ElBruno.S1Mini`'s C# code is MIT; the downloaded model weights remain under the upstream Apache-2.0 license. Vendor quality claim: **94.8% token accuracy on 7,519 held-out English cases** (Superwhisper's measurement, not independently re-verified here). **English only, v1.**

## Building from Source

```bash
git clone https://github.com/elbruno/ElBruno.S1Mini
cd ElBruno.S1Mini
dotnet build ElBruno.S1Mini.slnx
dotnet test ElBruno.S1Mini.slnx --framework net8.0
```

## What's New

- 🎉 **`v0.1.0`** — Initial release: `TranscriptNormalizer` API with styling/structure/context control line, chunking helper, and DI extension.
- 🧵 **`S1MiniClient`** — self-contained `IChatClient` implementation with automatic HuggingFace download of `elbruno/s1-mini-onnx` (int4).
- 🧪 **Qwen3 non-thinking prompt format** — ported verbatim from the model's own `chat_template.jinja`, verified byte-for-byte against the real model.
- 🦺 **ORT-GenAI temperature-0 crash guard** — the native `temperature=0` divide-by-zero trap is guarded at the runtime layer; greedy decoding is safe for every call.
- 🔬 **Empirically-verified control-line documentation** — enum XML docs and README table reflect what the real INT4 model actually does, including the `lists` / `message` / `notes` caveats.

## Documentation

- [Getting Started](docs/getting-started.md) — installation, first steps, configuration
- [Transcript Normalization](docs/transcript-normalization.md) — full API guide, control-line reference, empirical behavior table

## Samples

| Sample | Description |
|--------|-------------|
| [HelloS1Mini](src/samples/HelloS1Mini) | Console sample covering default normalization, `Context.Email`, `Structure.Lists`, and pure-filler input. |

## Testing

```bash
dotnet test ElBruno.S1Mini.slnx --framework net8.0
```

Tests use a fake `IChatClient` and a recording `IGenerationSearchOptions` seam — no model downloads, no network, no GPU required.

## 📄 License

MIT — see [LICENSE](LICENSE). The downloaded s1-mini model weights are Apache-2.0 (upstream), not MIT.

## 🙏 Acknowledgments

- [`superwhisper/s1-mini`](https://huggingface.co/superwhisper/s1-mini) — the fine-tuned ASR normalizer this library wraps. All model design and training credit belongs to the Superwhisper team.
- [`Qwen/Qwen3-0.6B`](https://huggingface.co/Qwen/Qwen3-0.6B) — the base model s1-mini is fine-tuned from.
- [ONNX Runtime GenAI](https://github.com/microsoft/onnxruntime-genai) — inference engine.
- [Hugging Face](https://huggingface.co/) — model hosting.
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) — the `IChatClient` contract this library implements.

## Related Projects

- [ElBruno.LocalLLMs](https://github.com/elbruno/ElBruno.LocalLLMs) — Run local LLMs in .NET
- [ElBruno.Whisper](https://github.com/elbruno/ElBruno.Whisper) — Local Whisper speech-to-text in .NET
- [ElBruno.HuggingFace](https://github.com/elbruno/ElBruno.HuggingFace) — HuggingFace model utilities for .NET

## 👋 About the Author

Hi! I'm **ElBruno** 🧡, a passionate developer and content creator exploring AI, .NET, and modern development practices.

- 💻 **Blog**: [ElBruno.com](https://elbruno.com)
- 📺 **YouTube**: [youtube.com/@inthelabs](https://youtube.com/@inthelabs)
- 🔗 **LinkedIn**: [linkedin.com/in/inthelabs](https://linkedin.com/in/inthelabs)
- 𝕏 **Twitter**: [@inthelabs](https://twitter.com/inthelabs)
- 🎙️ **Podcast**: [inthelabs.dev](https://inthelabs.dev)

**Made with ❤️ by [ElBruno](https://github.com/elbruno)**
