# Transcript Normalization (s1-mini)

`ElBruno.S1Mini.Normalization.TranscriptNormalizer` wraps [`superwhisper/s1-mini`](https://huggingface.co/superwhisper/s1-mini), a 0.6B `Qwen3ForCausalLM` fine-tune of `Qwen/Qwen3-0.6B`.

> ⚠️ **This is not a chat model.** s1-mini performs exactly one task: rewriting a raw, lowercase, unpunctuated speech-to-text transcript into clean written text. It does not answer questions, does not hold a conversation, and does not follow arbitrary instructions. Give it anything other than a transcript-to-clean and you will get unpredictable output.

## What it does

Given a raw ASR transcript, s1-mini:

- Removes filler words (`um`, `uh`, `like`, `you know`)
- Resolves self-corrections to what the speaker ultimately landed on (`"nine am no sorry ten thirty"` → `"10:30"`)
- Applies punctuation and capitalization
- Renders spoken numbers, dates, times, currency, and emails in written form (`"three thirty"` → `"3:30"`, `"bruno at example dot com"` → `"bruno@example.com"`)
- Returns an **empty string** for pure filler/noise input with nothing worth keeping

Quality: the vendor reports **94.8% token accuracy** on a held-out set of 7,519 English test cases (Superwhisper's own measurement — not independently re-verified by this project). **English only, v1.**

> **What that number applies to.** Superwhisper measured 94.8% on their official
> [GGUF Q4_K_M build](https://huggingface.co/superwhisper/s1-mini-GGUF), which their model card
> describes as "the build the published accuracy was measured on." This library runs a
> *different* quantization — the INT4 ONNX conversion in
> [`elbruno/s1-mini-onnx`](https://huggingface.co/elbruno/s1-mini-onnx) — whose accuracy has
> **not** been separately measured. Treat 94.8% as an upstream figure for the reference build,
> not as a verified property of the INT4 ONNX weights.

> **Not using .NET?** Superwhisper publishes official GGUF builds at
> [superwhisper/s1-mini-GGUF](https://huggingface.co/superwhisper/s1-mini-GGUF) for llama.cpp,
> Ollama, and LM Studio. This library targets ONNX Runtime GenAI instead, so the ONNX
> conversion is what it downloads.

## Required prompt format

s1-mini was fine-tuned against one exact system prompt plus a one-line control header. `TranscriptNormalizer` builds this for you, but it's worth understanding the wire format.

**System prompt** (verbatim, `TranscriptNormalizer.DefaultSystemPrompt`):

```text
You are a text normalizer for speech-to-text transcripts. The input begins with a control line
specifying the styling, structure, and context settings; clean the transcript to match those
settings and output only the cleaned text.
```

**User message** — a control line followed by the raw transcript:

```text
[Styling: semi-formal] [Structure: prose] [Context: general]
so um i need to like send the the report by uh friday no wait make that thursday
```

The full ChatML rendering ends with the Qwen3 non-thinking generation prompt:

```text
<|im_start|>assistant
<think>

</think>

```

The empty `<think>` block selects Qwen3's non-thinking mode (`enable_thinking=False`), which is what s1-mini expects. This was verified byte-for-byte against the model's own `chat_template.jinja` and reproduced 6/6 outputs identically against the real model.

## Quick start

```bash
dotnet add package ElBruno.S1Mini
```

```csharp
using ElBruno.S1Mini.Normalization;

using var normalizer = await TranscriptNormalizer.CreateAsync();

var cleaned = await normalizer.NormalizeAsync(
    "so um i need to like send the the report by uh friday no wait make that thursday");

Console.WriteLine(cleaned);
// "So I need to send the report by Thursday."
```

`TranscriptNormalizer.CreateAsync()` downloads the INT4 variant of `elbruno/s1-mini-onnx` (~500 MB) automatically on first use to `%LOCALAPPDATA%/ElBruno/S1Mini/models`. Subsequent runs load from cache.

### Bring your own IChatClient

`TranscriptNormalizer` is a pure control-line + prompt-builder layer over any `IChatClient`. You can use it with a different provider — as long as that client is really pointed at s1-mini.

```csharp
using ElBruno.S1Mini;
using ElBruno.S1Mini.Normalization;

using var chatClient = await S1MiniClient.CreateAsync();
using var normalizer = new TranscriptNormalizer(chatClient);
```

### Dependency injection

```csharp
services.AddTranscriptNormalizer();
```

`TranscriptNormalizer` is deliberately **not** registered as `IChatClient` — it isn't a general chat client, and registering it as one would mislead consumers expecting chat semantics.

## Control-line options

`TranscriptNormalizerOptions` exposes three enums mapped to the model's control line, plus generation settings:

```csharp
var options = new TranscriptNormalizerOptions
{
    Styling = TranscriptStyling.Formal,
    Structure = TranscriptStructure.Prose,
    Context = TranscriptContext.Email,
    MaxTokens = 1024, // default
};

var cleaned = await normalizer.NormalizeAsync(transcript, options);
```

The table below reflects **empirically verified behavior** against the converted INT4 model — not just what the enum names imply:

| Setting | Value | Wire value | Verified behavior |
|---|---|---|---|
| `Styling` | `SemiFormal` (default) | `semi-formal` | Baseline register — contractions kept, natural cleanup. |
| `Styling` | `Formal` | `formal` | **Distinct.** Expands contractions (`can't` → `cannot`, `I'll` → `I will`); clearly more formal register. |
| `Styling` | `Casual` | `casual` | **Distinct.** Keeps filler words and casing/contractions largely as-is; minimal cleanup, clearly casual register. |
| `Structure` | `Prose` (default) | `prose` | Continuous prose output. |
| `Structure` | `Lists` | `lists` | **Caveat:** did **not** reliably produce Markdown bullet/numbered output in testing, despite the model card describing this as possible. Output was reworded prose, not a literal list. Don't rely on this for structured/machine-parseable output. |
| `Context` | `General` (default) | `general` | Baseline — no added structure. |
| `Context` | `Email` | `email` | **Distinct.** Produces blank-line-separated greeting/body/sign-off structure. |
| `Context` | `Message` | `message` | **No-op.** Accepted without error but produces output identical to `General` — confirmed across two separate test transcripts. |
| `Context` | `Notes` | `notes` | **No-op.** Same as `Message` — accepted, but behaves identically to `General`. |

## Empty input, empty output

Pure filler/noise input correctly normalizes to an **empty string** — this is expected model behavior, not an error:

```csharp
var result = await normalizer.NormalizeAsync("um uh you know like");
// result == ""
```

`TranscriptNormalizer.NormalizeAsync` also short-circuits to `string.Empty` for empty/whitespace input without calling the model at all.

## Chunking long transcripts

s1-mini's recommended input length is **~1,000 tokens**. For longer transcripts, use `NormalizeChunkedAsync`, which splits at sentence boundaries and normalizes each chunk independently:

```csharp
var cleaned = await normalizer.NormalizeChunkedAsync(longTranscript, maxCharsPerChunk: 3500);
```

This is a **best-effort convenience**, not authoritative chunking — each chunk is normalized statelessly with no visibility into neighboring chunks, so context that spans a chunk boundary (e.g. a self-correction split across chunks) can be handled poorly. For transcripts with that kind of structure, chunk manually at a natural pause in the audio instead.

## Temperature 0 = greedy decoding (safe by design)

`TranscriptNormalizer.NormalizeAsync` sets `ChatOptions.Temperature = 0f` to select greedy decoding, matching the model card's `do_sample=False` recommendation. This is safe: `S1MiniClient`'s runtime layer treats `Temperature <= 0` as the library-wide "greedy" contract and **omits** the native `temperature` search option entirely in that case (it only calls `SetSearchOption("temperature", ...)` when `Temperature > 0`), relying on `do_sample=False` alone to select greedy decoding.

This matters because `onnxruntime-genai`'s native runtime has a real crash trap here: passing a literal `temperature=0.0` straight into its search options crashes with an integer divide-by-zero, even when `do_sample=False`. This library guards against that at the runtime layer, so callers can safely use `Temperature = 0f` — and must, to preserve greedy decoding.

## FP16 variant — currently non-functional on CPU

`elbruno/s1-mini-onnx` also publishes an `fp16/` subfolder, but that variant is **broken on CPU** with `onnxruntime-genai` 0.15.1: every prompt fails with a shape mismatch in the GQA `repeat_kv` Reshape node. This is an ONNX Runtime builder/graph-optimizer bug for FP16 + GQA on the CPU execution provider — not something specific to this conversion. **Prefer INT4 for all current use.** `S1MiniOptions.ModelSubPath` defaults to `int4` for this reason.

## Model license

`superwhisper/s1-mini` is licensed **Apache-2.0** with a naming clause that discourages using the "superwhisper" or "s1-mini" name for derivative products. This project's converted ONNX artifacts (`elbruno/s1-mini-onnx`) are an **explicitly unofficial, unaffiliated, non-endorsed derivative** provided for convenience. `ElBruno.S1Mini`'s own C# code is MIT-licensed; the model weights it downloads remain under their upstream Apache-2.0 license.
