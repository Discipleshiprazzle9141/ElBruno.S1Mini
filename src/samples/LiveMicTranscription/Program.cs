using System.Threading.Channels;
using ElBruno.HuggingFace;
using ElBruno.Realtime;
using ElBruno.Realtime.SileroVad;
using ElBruno.S1Mini;
using ElBruno.S1Mini.Normalization;
using ElBruno.Whisper;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Spectre.Console;
using Spectre.Console.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// Live microphone → Whisper → s1-mini, 100% local.
//
// Two ElBruno libraries, two models, one pipeline:
//
//   default microphone (NAudio, 16 kHz mono PCM)
//        → ElBruno.Whisper   — speech-to-text, produces the RAW transcript
//        → ElBruno.S1Mini    — transcript normalizer, produces the CLEAN text
//
// s1-mini is not an audio model: it only rewrites an existing ASR transcript
// (fillers removed, self-corrections resolved, punctuation/capitalization
// applied). Whisper supplies the transcript; s1-mini cleans it up.
//
// Both models are downloaded from HuggingFace on first run and then run
// on-device through ONNX Runtime. Nothing is sent to a cloud service.
//
// Windows-only sample (NAudio's WaveInEvent capture is Windows-only).
// ─────────────────────────────────────────────────────────────────────────────

if (!OperatingSystem.IsWindows())
{
    AnsiConsole.MarkupLine("[red]This sample requires Windows (NAudio microphone capture is Windows-only).[/]");
    return;
}

const int SampleRate = 16_000;      // Whisper always wants 16 kHz mono.
const int FrameMilliseconds = 100;

// ── Command-line options ────────────────────────────────────────────────────
//
//   --save-audio          write every captured utterance to a .wav file
//   --wav <file|folder>   transcribe a recording instead of the microphone
//
// The two together make the pipeline reproducible: capture a session once, then
// replay the exact same audio while changing models or settings, so differences
// come from the change under test rather than from a new performance.
var saveAudio = args.Any(a => a.Equals("--save-audio", StringComparison.OrdinalIgnoreCase));

var wavIndex = Array.FindIndex(args, a => a.Equals("--wav", StringComparison.OrdinalIgnoreCase));
var wavInput = wavIndex >= 0 && wavIndex + 1 < args.Length ? args[wavIndex + 1] : null;

if (wavIndex >= 0 && wavInput is null)
{
    AnsiConsole.MarkupLine("[red]--wav needs a path:[/] --wav recording.wav [grey](or a folder of .wav files)[/]");
    return;
}

var recordingsDirectory = Path.Combine(AppContext.BaseDirectory, "recordings");

// A crash in a background task (audio capture, VAD stream) must never look like
// the app simply "closed" — surface it instead of exiting silently.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.WriteLine();
    Console.WriteLine($"❌ Unexpected error: {(e.ExceptionObject as Exception)?.Message}");
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine();
    Console.WriteLine($"❌ Background task error: {e.Exception.GetBaseException().Message}");
    e.SetObserved();
};

if (wavInput is null && WaveInEvent.DeviceCount == 0)
{
    AnsiConsole.MarkupLine("[red]No microphone found.[/] Plug in a recording device and try again.");
    return;
}

var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

// ── 1. Show the pipeline, then pick the two models ──────────────────────────

AnsiConsole.Write(
    new FigletText("s1-mini").Color(Color.DeepSkyBlue1));

var pipeline = new Grid()
    .AddColumn(new GridColumn().NoWrap().PadRight(2))
    .AddColumn()
    .AddRow("[grey]🎤 microphone[/]", "[grey]default capture device[/]")
    .AddRow("[deepskyblue1]→ 🧠 Silero VAD v5[/]", "speech detection [grey](automatic, 2 MB)[/]")
    .AddRow("[deepskyblue1]→ 📝 Whisper[/]", "speech-to-text [grey](choose below)[/]")
    .AddRow("[deepskyblue1]→ ✨ s1-mini[/]", "transcript cleanup [grey](choose below)[/]");

AnsiConsole.Write(
    new Panel(pipeline)
        .Header("[bold] Live local transcription [/]")
        .BorderColor(Color.DeepSkyBlue1)
        .RoundedBorder());

AnsiConsole.MarkupLine(
    "[grey]Speech boundaries come from a neural detector, so quiet fillers ([/]\"umm\", \"ehh\"[grey])[/]");
AnsiConsole.MarkupLine(
    "[grey]reach Whisper instead of being gated out as noise. Everything runs on-device.[/]");
AnsiConsole.WriteLine();

var whisperModel = PromptForWhisperModel();
var (styleLabel, normalizerOptions) = PromptForOutputStyle();

AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule("[deepskyblue1]Loading models[/]").LeftJustified());

var selection = new Table()
    .Border(TableBorder.SimpleHeavy)
    .BorderColor(Color.Grey)
    .AddColumn("[grey]Stage[/]")
    .AddColumn("[grey]Selection[/]")
    .AddRow("📝 Whisper", $"[bold]{Markup.Escape(whisperModel.DisplayName)}[/]")
    .AddRow("✨ s1-mini", $"[bold]{Markup.Escape(styleLabel)}[/]");

AnsiConsole.Write(selection);

// ── 2. Load Whisper (downloads on first run) ────────────────────────────────

var whisperCacheDirectory = Path.Combine(localAppData, "ElBruno", "Whisper", "models");
var whisperModelDirectory = Path.Combine(whisperCacheDirectory, whisperModel.Id);

var whisperOptions = new WhisperOptions
{
    Model = whisperModel,
    CacheDirectory = whisperCacheDirectory
};

var whisperCached = IsWhisperModelCached(whisperModelDirectory);

AnsiConsole.WriteLine();

var whisper = whisperCached
    ? await WhisperClient.CreateAsync(whisperOptions)
    : await DownloadWithProgressAsync(
        $"📝 {whisperModel.DisplayName}",
        reporter => WhisperClient.CreateAsync(whisperOptions, reporter));

AnsiConsole.MarkupLine(whisperCached
    ? $"[green]✔[/] {Markup.Escape(whisperModel.DisplayName)} [grey](cached)[/]"
    : $"[green]✔[/] {Markup.Escape(whisperModel.DisplayName)} [grey](downloaded)[/]");

// ── 3. Load s1-mini (downloads on first run) ────────────────────────────────

var s1MiniOptions = new S1MiniOptions();
string? s1MiniModelDirectory = null;
var s1MiniCached = true;

var localS1MiniPath = Environment.GetEnvironmentVariable("S1MINI_MODEL_PATH");
if (!string.IsNullOrWhiteSpace(localS1MiniPath))
{
    // User supplied their own model — not ours to download or delete.
    s1MiniOptions.ModelPath = localS1MiniPath;
    s1MiniOptions.EnsureModelDownloaded = false;
    AnsiConsole.MarkupLine(
        $"[grey]Using s1-mini from S1MINI_MODEL_PATH: {Markup.Escape(localS1MiniPath)}[/]");
}
else
{
    var s1MiniCacheDirectory = Path.Combine(localAppData, "ElBruno", "S1Mini", "models");
    s1MiniOptions.CacheDirectory = s1MiniCacheDirectory;
    s1MiniModelDirectory = Path.Combine(
        s1MiniCacheDirectory,
        s1MiniOptions.RepoId.Replace('/', '_').Replace('\\', '_'));

    var s1MiniFiles = Path.Combine(s1MiniModelDirectory, s1MiniOptions.ModelSubPath);
    s1MiniCached = File.Exists(Path.Combine(s1MiniFiles, "genai_config.json"));
}

var normalizer = s1MiniCached
    ? await TranscriptNormalizer.CreateAsync(s1MiniOptions)
    : await DownloadWithProgressAsync("✨ s1-mini", reporter =>
    {
        s1MiniOptions.DownloadProgress = reporter;
        return TranscriptNormalizer.CreateAsync(s1MiniOptions);
    });

AnsiConsole.MarkupLine(s1MiniCached
    ? "[green]✔[/] s1-mini [grey](cached)[/]"
    : "[green]✔[/] s1-mini [grey](downloaded)[/]");

// ── 4. Capture the microphone and run the pipeline ──────────────────────────

// ── Voice activity detection (Silero VAD v5, neural) ────────────────────────
//
// Speech boundaries come from a small neural model rather than a raw energy
// threshold, and that distinction is the whole point of this sample. Filler
// sounds — "ummm", "ehhh", a trailing-off "so..." — are LOW ENERGY and sit right
// on top of the noise floor. An energy gate throws away exactly the words that
// s1-mini exists to clean up, so the demo appears to do nothing. Silero
// classifies those sounds as speech.
//
// Silero reports the START and END time of every speech segment. Rather than
// stitching the segments together (which deletes the quiet audio *between* them
// and mangles the prosody), each utterance is cut as ONE CONTIGUOUS SLICE of the
// original stream — first segment start to last segment end, plus padding.
// Whisper then transcribes the phrase verbatim, fillers included.
// A hesitation ("I think... ummm... we should") contains a pause that is often
// longer than a second. Cutting the phrase there splits one sentence into
// fragments, strands the filler at a boundary, and leaves s1-mini too little
// context to clean anything up — so the gap is deliberately generous.
const int UtteranceGapMs = 1_500;       // Silence between segments that ends a phrase.
const int SlicePaddingMs = 400;         // Retained on both sides of the detected speech.
const int MinimumUtteranceMs = 400;     // Ignore coughs and clicks shorter than this.
const int MaximumUtteranceMs = 30_000;  // Whisper's context window.
const int TimelineRetentionMs = 90_000; // Bound the rolling audio buffer.

AnsiConsole.WriteLine();

var vadCacheDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ElBruno",
    "SileroVad");

var vad = await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("deepskyblue1"))
    .StartAsync("Loading Silero VAD (2 MB, first run only)...",
        _ => Task.FromResult(new SileroVadDetector(vadCacheDirectory)));

AnsiConsole.MarkupLine("[green]✔[/] Silero VAD v5 [grey](neural speech detection)[/]");

// The rolling timeline holds every captured sample so an utterance can be sliced
// back out by timestamp. `timelineBaseSample` is the absolute index of timeline[0].
var timeline = new List<float>();
var timelineBaseSample = 0L;
var totalSamplesCaptured = 0L;
var timelineGate = new object();

var audioChannel = Channel.CreateUnbounded<byte[]>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

double? pendingStartMs = null;
double? pendingEndMs = null;
var speechActive = false;
var listening = false;
var processing = new SemaphoreSlim(1, 1);

using var waveIn = new WaveInEvent
{
    // -1 is WAVE_MAPPER — the device Windows considers the default. Device 0 is
    // merely the first enumerated device, which is often NOT the default one.
    DeviceNumber = -1,
    WaveFormat = new WaveFormat(SampleRate, 16, 1),
    BufferMilliseconds = FrameMilliseconds
};

waveIn.RecordingStopped += (_, e) =>
{
    if (e.Exception is not null)
    {
        ConsoleOut.Line($"[yellow]⚠[/] Recording stopped unexpectedly: {Markup.Escape(e.Exception.Message)}");
    }
};

waveIn.DataAvailable += (_, e) =>
{
    var sampleCount = e.BytesRecorded / 2;
    if (sampleCount == 0 || !listening)
    {
        return;
    }

    // Int16 PCM → normalized float32, which is what WhisperClient expects.
    var samples = new float[sampleCount];
    double sumOfSquares = 0;

    for (var i = 0; i < sampleCount; i++)
    {
        var sample = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
        samples[i] = sample;
        sumOfSquares += sample * sample;
    }

    lock (timelineGate)
    {
        timeline.AddRange(samples);
        totalSamplesCaptured += sampleCount;

        // Trim history, but never past audio an unflushed utterance still needs.
        var retain = (long)TimelineRetentionMs * SampleRate / 1000;
        var mustKeepFrom = pendingStartMs is double startMs
            ? (long)((startMs - SlicePaddingMs) * SampleRate / 1000)
            : long.MaxValue;
        var trimTo = Math.Min(totalSamplesCaptured - retain, mustKeepFrom);
        var dropCount = (int)Math.Min(Math.Max(0, trimTo - timelineBaseSample), timeline.Count);

        if (dropCount > 0)
        {
            timeline.RemoveRange(0, dropCount);
            timelineBaseSample += dropCount;
        }
    }

    ConsoleOut.Meter(Math.Sqrt(sumOfSquares / sampleCount), speechActive);

    // Silero consumes the raw PCM bytes; hand it a copy since NAudio reuses the buffer.
    var pcm = new byte[e.BytesRecorded];
    Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);
    audioChannel.Writer.TryWrite(pcm);
};

// Cuts [start, end] out of the rolling timeline and sends it down the pipeline.
void FlushPendingUtterance()
{
    if (pendingStartMs is not double startMs || pendingEndMs is not double endMs)
    {
        return;
    }

    pendingStartMs = null;
    pendingEndMs = null;
    speechActive = false;

    if (endMs - startMs < MinimumUtteranceMs)
    {
        return;
    }

    float[] slice;

    lock (timelineGate)
    {
        var from = (long)Math.Max(0, (startMs - SlicePaddingMs) * SampleRate / 1000);
        var to = (long)((endMs + SlicePaddingMs) * SampleRate / 1000);

        var localFrom = (int)Math.Clamp(from - timelineBaseSample, 0, timeline.Count);
        var localTo = (int)Math.Clamp(to - timelineBaseSample, localFrom, timeline.Count);
        var length = Math.Min(localTo - localFrom, MaximumUtteranceMs * SampleRate / 1000);

        if (length <= 0)
        {
            return;
        }

        slice = new float[length];
        timeline.CopyTo(localFrom, slice, 0, length);
    }

    _ = ProcessUtteranceAsync(slice);
}

// Consume Silero's segment stream and group segments into utterances.
var vadOptions = new VadOptions { SampleRate = SampleRate, Channels = 1 };

var vadTask = Task.Run(async () =>
{
    try
    {
        await foreach (var segment in vad.DetectSpeechAsync(
            audioChannel.Reader.ReadAllAsync(), vadOptions))
        {
            var startMs = segment.StartTime.TotalMilliseconds;
            var endMs = segment.EndTime.TotalMilliseconds;

            // A long gap since the previous segment means the phrase ended.
            if (pendingEndMs is double previousEnd && startMs - previousEnd > UtteranceGapMs)
            {
                FlushPendingUtterance();
            }

            pendingStartMs ??= startMs;
            pendingEndMs = endMs;
            speechActive = true;
        }
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown.
    }
    catch (Exception ex)
    {
        ConsoleOut.Line($"[yellow]⚠[/] Voice detection error: {Markup.Escape(ex.Message)}");
    }
});

// Silero only yields once a segment closes, so a watchdog on the AUDIO clock
// (not wall clock) decides when a phrase has gone quiet long enough to flush.
var watchdogCts = new CancellationTokenSource();

var watchdogTask = Task.Run(async () =>
{
    try
    {
        while (!watchdogCts.IsCancellationRequested)
        {
            await Task.Delay(100, watchdogCts.Token);

            if (pendingEndMs is not double endMs)
            {
                continue;
            }

            long captured;
            lock (timelineGate)
            {
                captured = totalSamplesCaptured;
            }

            var positionMs = captured * 1000d / SampleRate;

            if (positionMs - endMs > UtteranceGapMs + SlicePaddingMs)
            {
                FlushPendingUtterance();
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown.
    }
});

Console.WriteLine();

if (wavInput is not null)
{
    // ── Replay mode: feed a recording through the identical pipeline ─────────
    var files = Directory.Exists(wavInput)
        ? Directory.GetFiles(wavInput, "*.wav").OrderBy(f => f).ToArray()
        : File.Exists(wavInput) ? [wavInput] : [];

    if (files.Length == 0)
    {
        AnsiConsole.MarkupLine($"[red]No .wav file found at[/] {Markup.Escape(wavInput)}");
        return;
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(
        new Panel(new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                .AddColumn()
                .AddRow("[grey]📼 recordings[/]", $"[white]{files.Length}[/] [grey]file(s)[/]")
                .AddRow("[grey]📝 Whisper[/]", $"[white]{Markup.Escape(whisperModel.DisplayName)}[/]")
                .AddRow("[grey]✨ s1-mini[/]", $"[white]{Markup.Escape(styleLabel)}[/]"))
            .Header("[bold green] Replay — same VAD and models as live capture [/]")
            .BorderColor(Color.Green)
            .RoundedBorder());
    AnsiConsole.WriteLine();

    listening = true;

    foreach (var file in files)
    {
        AnsiConsole.Write(
            new Rule($"[deepskyblue1]{Markup.Escape(Path.GetFileName(file))}[/]").LeftJustified());

        float[] samples;

        try
        {
            samples = ReadWavAsMono16k(file);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠[/] Could not read: {Markup.Escape(ex.Message)}");
            continue;
        }

        // Push through the same channel the microphone uses, in the same frame
        // size, so the VAD sees an identical stream shape.
        var frame = FrameMilliseconds * SampleRate / 1000;

        for (var offset = 0; offset < samples.Length; offset += frame)
        {
            var count = Math.Min(frame, samples.Length - offset);
            var pcm = new byte[count * 2];

            for (var i = 0; i < count; i++)
            {
                var value = (short)Math.Clamp(samples[offset + i] * 32768f, short.MinValue, short.MaxValue);
                BitConverter.GetBytes(value).CopyTo(pcm, i * 2);
            }

            lock (timelineGate)
            {
                timeline.AddRange(samples.AsSpan(offset, count).ToArray());
                totalSamplesCaptured += count;
            }

            audioChannel.Writer.TryWrite(pcm);
        }

        // Trailing silence so the VAD closes the final segment.
        var tail = new byte[SampleRate * 2];
        audioChannel.Writer.TryWrite(tail);

        lock (timelineGate)
        {
            totalSamplesCaptured += SampleRate;
        }

        // Let the VAD drain and the watchdog flush before the next file.
        await Task.Delay(1_500);
        FlushPendingUtterance();
        await processing.WaitAsync();
        processing.Release();
    }

    audioChannel.Writer.TryComplete();
    await Task.WhenAny(vadTask, Task.Delay(3_000));
    FlushPendingUtterance();

    await processing.WaitAsync();
    processing.Release();
}
else
{
    waveIn.StartRecording();

    listening = true;

    var listeningInfo = new Grid()
        .AddColumn(new GridColumn().NoWrap().PadRight(2))
        .AddColumn()
        .AddRow("[grey]🎤 microphone[/]", $"[white]{Markup.Escape(DescribeDefaultMicrophone())}[/]")
        .AddRow("[grey]📝 Whisper[/]", $"[white]{Markup.Escape(whisperModel.DisplayName)}[/]")
        .AddRow("[grey]✨ s1-mini[/]", $"[white]{Markup.Escape(styleLabel)}[/]");

    if (saveAudio)
    {
        Directory.CreateDirectory(recordingsDirectory);
        listeningInfo.AddRow("[grey]💾 recordings[/]", $"[dim]{Markup.Escape(recordingsDirectory)}[/]");
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(
        new Panel(listeningInfo)
            .Header("[bold green] Listening — speak naturally, press Enter to stop [/]")
            .BorderColor(Color.Green)
            .RoundedBorder());

    AnsiConsole.MarkupLine(
        "[grey]Speech boundaries come from Silero VAD, so quiet fillers are kept, not gated out.[/]");
    AnsiConsole.WriteLine();

    var exitSignal = new TaskCompletionSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        exitSignal.TrySetResult();
    };

    _ = Task.Run(() =>
    {
        Console.ReadLine();
        exitSignal.TrySetResult();
    });

    await exitSignal.Task;

    listening = false;
    waveIn.StopRecording();

    // Drain: close the audio stream so Silero finishes, then flush any last phrase.
    audioChannel.Writer.TryComplete();
    await Task.WhenAny(vadTask, Task.Delay(3_000));

    FlushPendingUtterance();
}

await watchdogCts.CancelAsync();
await Task.WhenAny(watchdogTask, Task.Delay(1_000));

ConsoleOut.ClearMeter();
AnsiConsole.MarkupLine("[grey]Stopped listening.[/]");

// Let any in-flight transcription finish before tearing the models down.
await processing.WaitAsync();
processing.Release();

// Release ONNX file handles before offering to delete the models — the files
// stay locked on Windows until every client is disposed.
normalizer.Dispose();
whisper.Dispose();
vad.Dispose();

// ── 5. Offer to clean up the downloaded models ──────────────────────────────

OfferToDeleteModels(
    (whisperModel.DisplayName, whisperModelDirectory),
    ("s1-mini", s1MiniModelDirectory),
    ("Silero VAD", vadCacheDirectory));

AnsiConsole.MarkupLine("[deepskyblue1]Bye![/]");

// ─────────────────────────────────────────────────────────────────────────────

async Task ProcessUtteranceAsync(float[] samples)
{
    // One utterance at a time: both models are CPU-bound, and overlapping runs
    // would interleave console output.
    await processing.WaitAsync();
    try
    {
        // Quiet capture devices deliver speech peaking around 0.01 instead of
        // near 1.0. Whisper is far less accurate on such a weak signal, so scale
        // each utterance up to a healthy level before transcribing.
        NormalizeVolume(samples);

        if (saveAudio)
        {
            try
            {
                Directory.CreateDirectory(recordingsDirectory);

                var path = Path.Combine(
                    recordingsDirectory,
                    $"utterance-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav");

                WriteWav(path, samples, SampleRate);
                ConsoleOut.Line($"[grey]💾 saved {Markup.Escape(path)}[/]");
            }
            catch (Exception ex)
            {
                ConsoleOut.Line($"[yellow]⚠[/] Could not save audio: {Markup.Escape(ex.Message)}");
            }
        }

        var transcription = await whisper.TranscribeAsync(samples.AsMemory(), SampleRate);
        var raw = transcription.Text?.Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var cleaned = (await normalizer.NormalizeAsync(raw, normalizerOptions))?.Trim();

        var durationSeconds = samples.Length / (double)SampleRate;

        string cleanedMarkup;
        string footer;

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleanedMarkup = "[grey italic](filler only — nothing to keep)[/]";
            footer = "[grey]s1-mini removed everything[/]";
        }
        else if (string.Equals(cleaned, raw, StringComparison.Ordinal))
        {
            // Expected on already-tidy transcripts; explain rather than look broken.
            cleanedMarkup = $"[white]{Markup.Escape(cleaned)}[/]";
            footer = "[grey]unchanged — the transcript was already clean[/]";
        }
        else
        {
            cleanedMarkup = $"[bold green]{Markup.Escape(cleaned)}[/]";
            footer = $"[grey]{raw.Length} → {cleaned.Length} chars[/]";
        }

        var table = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(2))
            .AddColumn()
            .AddRow("[grey]🎙️  raw[/]", $"[grey]{Markup.Escape(raw)}[/]")
            .AddRow("[deepskyblue1]✨ clean[/]", cleanedMarkup);

        ConsoleOut.Render(
            new Panel(table)
                .Header($"[grey] {durationSeconds:0.0}s · {footer} [/]")
                .BorderColor(Color.Grey35)
                .RoundedBorder()
                .Expand());
    }
    catch (Exception ex)
    {
        ConsoleOut.Line($"[red]⚠️  Pipeline error:[/] {Markup.Escape(ex.Message)}");
    }
    finally
    {
        processing.Release();
    }
}

/// <summary>
/// Reads any .wav file as 16 kHz mono float samples, resampling and downmixing
/// as needed so a recording made with any tool can be replayed through the
/// same pipeline as live microphone input.
/// </summary>
static float[] ReadWavAsMono16k(string path)
{
    using var reader = new AudioFileReader(path);

    ISampleProvider provider = reader;

    if (provider.WaveFormat.Channels > 1)
    {
        provider = provider.ToMono();
    }

    if (provider.WaveFormat.SampleRate != 16_000)
    {
        provider = new WdlResamplingSampleProvider(provider, 16_000);
    }

    var samples = new List<float>();
    var buffer = new float[16_000];
    int read;

    while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
    {
        samples.AddRange(buffer.AsSpan(0, read).ToArray());
    }

    return samples.ToArray();
}

/// <summary>Writes float samples as a 16-bit PCM mono .wav file.</summary>
static void WriteWav(string path, float[] samples, int sampleRate)
{
    using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));

    foreach (var sample in samples)
    {
        writer.WriteSample(Math.Clamp(sample, -1f, 1f));
    }
}

static void NormalizeVolume(float[] samples)
{
    const float TargetPeak = 0.95f;
    const float MaximumGain = 40f;

    var peak = 0f;
    foreach (var sample in samples)
    {
        peak = Math.Max(peak, Math.Abs(sample));
    }

    if (peak <= 0.0001f || peak >= TargetPeak)
    {
        return;
    }

    var gain = Math.Min(TargetPeak / peak, MaximumGain);
    for (var i = 0; i < samples.Length; i++)
    {
        samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
    }
}

static (string Label, TranscriptNormalizerOptions Options) PromptForOutputStyle()
{
    // Whisper does transcribe "um"/"uh" when they are actually captured, so plain
    // cleanup is a real demo — but larger Whisper models tidy more aggressively, and
    // a clean sentence leaves little to remove. The options below are the ones this
    // repo has empirically verified to change s1-mini's output (see TranscriptStyling /
    // TranscriptContext docs — Message, Notes and Lists are documented no-ops).
    var choices = new (string Label, string Description, TranscriptNormalizerOptions Options)[]
    {
        ("General cleanup", "removes fillers, fixes punctuation (semi-formal prose)",
            new TranscriptNormalizerOptions()),
        ("Formal", "removes fillers and expands contractions",
            new TranscriptNormalizerOptions { Styling = TranscriptStyling.Formal }),
        ("Email", "removes fillers and adds greeting / body structure",
            new TranscriptNormalizerOptions { Context = TranscriptContext.Email }),
        ("Formal email", "both of the above combined",
            new TranscriptNormalizerOptions
            {
                Styling = TranscriptStyling.Formal,
                Context = TranscriptContext.Email
            }),
        ("Casual", "keeps fillers and contractions, minimal cleanup",
            new TranscriptNormalizerOptions { Styling = TranscriptStyling.Casual }),
    };

    const int DefaultIndex = 0;   // General cleanup — filler removal is the headline feature.

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[deepskyblue1]Transcript cleanup[/]").LeftJustified());
    AnsiConsole.MarkupLine(
        "[grey]Tip: say \"so, um, hello, I have a question\" to see filler removal clearly.[/]");
    AnsiConsole.MarkupLine(
        "[grey]Smaller Whisper models keep more fillers; larger ones tidy as they go.[/]");
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Profile.Capabilities.Interactive)
    {
        var fallback = choices[DefaultIndex];
        AnsiConsole.MarkupLine(
            $"[grey]Non-interactive input — using default style: {Markup.Escape(fallback.Label)}[/]");
        return (fallback.Label, fallback.Options);
    }

    var selected = AnsiConsole.Prompt(
        new SelectionPrompt<(string Label, string Description, TranscriptNormalizerOptions Options)>()
            .Title("What should [deepskyblue1]s1-mini[/] do with the transcript?")
            .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
            .UseConverter(c =>
                $"{c.Label,-15} [grey]{Markup.Escape(c.Description)}[/]"
                + (c.Label == choices[DefaultIndex].Label ? " [dim](default)[/]" : string.Empty))
            .AddChoices(choices));

    return (selected.Label, selected.Options);
}

/// <summary>
/// A model counts as cached only when real weight files are present and no partial
/// <c>.tmp</c> download is left over. Merely checking that the folder exists reports
/// "already downloaded" for an interrupted download, which is misleading.
/// </summary>
static bool IsWhisperModelCached(string directory)
{
    if (!Directory.Exists(directory))
    {
        return false;
    }

    var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();

    return files.Any(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        && !files.Any(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
}

static string DescribeDefaultMicrophone()
{
    try
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        return device.FriendlyName;
    }
    catch
    {
        // CoreAudio can be unavailable (some server SKUs); fall back to WaveIn caps.
        return WaveInEvent.DeviceCount > 0
            ? WaveInEvent.GetCapabilities(0).ProductName
            : "unknown device";
    }
}

static WhisperModelDefinition PromptForWhisperModel()
{
    var models = KnownWhisperModels.All;

    // Whisper Tiny (English) — index 0. Empirically it preserves spoken fillers
    // ("um", "uh") that larger models tidy away, which is exactly what s1-mini is
    // meant to clean up. It is also the fastest to download and to run.
    var defaultModel = models[0];

    AnsiConsole.Write(new Rule("[deepskyblue1]Speech-to-text[/]").LeftJustified());

    // Interactive selection needs a real terminal; piped/redirected input (smoke tests,
    // CI) silently takes the default instead of throwing.
    if (!AnsiConsole.Profile.Capabilities.Interactive)
    {
        AnsiConsole.MarkupLine(
            $"[grey]Non-interactive input — using default Whisper model: {Markup.Escape(defaultModel.DisplayName)}[/]");
        return defaultModel;
    }

    return AnsiConsole.Prompt(
        new SelectionPrompt<WhisperModelDefinition>()
            .Title("Choose a local [deepskyblue1]Whisper[/] model:")
            .PageSize(12)
            .MoreChoicesText("[grey](move up and down to see more models)[/]")
            .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
            .UseConverter(m =>
            {
                var tag = m.IsMultilingual ? "multilingual" : "English only";
                var marker = ReferenceEquals(m, defaultModel) ? " [dim](default)[/]" : string.Empty;
                return $"{Markup.Escape(m.DisplayName),-38} [grey]{tag}[/]{marker}";
            })
            .AddChoices(models));
}

static void OfferToDeleteModels(params (string Name, string? Directory)[] models)
{
    var present = models
        .Where(m => m.Directory is not null && System.IO.Directory.Exists(m.Directory))
        .Select(m => (m.Name, Directory: m.Directory!, Size: TryGetDirectorySize(m.Directory!)))
        .ToArray();

    if (present.Length == 0)
    {
        return;
    }

    var totalSize = present.Sum(m => m.Size);

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[deepskyblue1]Downloaded models[/]").LeftJustified());

    var table = new Table()
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Grey35)
        .AddColumn("[grey]Model[/]")
        .AddColumn(new TableColumn("[grey]Size[/]").RightAligned())
        .AddColumn("[grey]Location[/]");

    foreach (var model in present)
    {
        table.AddRow(
            Markup.Escape(model.Name),
            $"[grey]{FormatSize(model.Size)}[/]",
            $"[dim]{Markup.Escape(model.Directory)}[/]");
    }

    table.AddRow("[bold]Total[/]", $"[bold]{FormatSize(totalSize)}[/]", string.Empty);
    AnsiConsole.Write(table);

    bool deleteThem;

    if (AnsiConsole.Profile.Capabilities.Interactive)
    {
        deleteThem = AnsiConsole.Prompt(
            new ConfirmationPrompt($"Delete all downloaded models ([bold]{FormatSize(totalSize)}[/])?")
            {
                DefaultValue = true
            });
    }
    else
    {
        // Piped/redirected stdin: still honour a scripted answer, but default to YES
        // when there is nothing to read (closed stdin, CI).
        AnsiConsole.Markup($"Delete all downloaded models ([bold]{FormatSize(totalSize)}[/])? [[y/n]] (y): ");

        var answer = Console.ReadLine()?.Trim();
        deleteThem = string.IsNullOrEmpty(answer)
            || answer.StartsWith("y", StringComparison.OrdinalIgnoreCase);

        AnsiConsole.WriteLine(deleteThem ? "y" : "n");
    }

    if (!deleteThem)
    {
        AnsiConsole.MarkupLine(
            "[grey]Keeping the downloaded models — the next run will start instantly.[/]");
        return;
    }

    foreach (var model in present)
    {
        try
        {
            System.IO.Directory.Delete(model.Directory, recursive: true);
            AnsiConsole.MarkupLine(
                $"[green]✔[/] Deleted {Markup.Escape(model.Name)} [grey]({FormatSize(model.Size)})[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]⚠[/] Could not delete {Markup.Escape(model.Name)}: {Markup.Escape(ex.Message)}");
        }
    }
}

static long TryGetDirectorySize(string directory)
{
    try
    {
        return new DirectoryInfo(directory)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }
    catch
    {
        return 0;
    }
}

static string FormatSize(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    double size = bytes;
    var unit = 0;

    while (size >= 1024 && unit < units.Length - 1)
    {
        size /= 1024;
        unit++;
    }

    return $"{size:0.#} {units[unit]}";
}

/// <summary>
/// Runs a model-loading operation behind a Spectre.Console progress bar, adapting the
/// library's <see cref="DownloadProgress"/> callbacks onto a <see cref="ProgressTask"/>.
/// </summary>
static async Task<T> DownloadWithProgressAsync<T>(
    string label,
    Func<IProgress<DownloadProgress>, Task<T>> factory)
{
    T result = default!;

    await AnsiConsole.Progress()
        .AutoClear(false)
        .HideCompleted(false)
        .Columns(
            new TaskDescriptionColumn { Alignment = Justify.Left },
            new ProgressBarColumn(),
            new PercentageColumn(),
            new DownloadedColumn(),
            new SpinnerColumn(Spinner.Known.Dots))
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask($"[deepskyblue1]{Markup.Escape(label)}[/]", autoStart: true, maxValue: 100);
            var reporter = new SpectreDownloadProgress(task, label);

            try
            {
                result = await factory(reporter);
            }
            finally
            {
                task.Value = task.MaxValue;
                task.StopTask();
            }
        });

    return result;
}

/// <summary>
/// Serializes console output between the audio callback thread (which paints a live
/// input-level meter in place) and the transcription task (which renders results
/// through Spectre.Console). Without this the meter overwrites transcripts mid-line.
/// </summary>
internal static class ConsoleOut
{
    private const int MeterWidth = 30;

    private static readonly object Gate = new();
    private static readonly bool CanRewriteLine = !Console.IsOutputRedirected;

    private static bool _meterVisible;
    private static int _lastLevelBlocks = -1;
    private static bool _lastSpeech;

    /// <summary>Paints the live input level plus a speech indicator driven by the neural VAD.</summary>
    public static void Meter(double rms, bool speech)
    {
        if (!CanRewriteLine)
        {
            return;
        }

        // Log-ish scaling: quiet capture devices (remote-desktop audio channels,
        // low-gain headsets) would otherwise never move a linear meter.
        var blocks = Math.Clamp((int)Math.Round(Math.Sqrt(rms / 0.15) * MeterWidth), 0, MeterWidth);

        lock (Gate)
        {
            if (blocks == _lastLevelBlocks && speech == _lastSpeech && _meterVisible)
            {
                return;
            }

            _lastLevelBlocks = blocks;
            _lastSpeech = speech;

            var bar = new char[MeterWidth];
            for (var i = 0; i < MeterWidth; i++)
            {
                bar[i] = i < blocks ? '█' : '·';
            }

            var state = speech ? "🗣  speech " : "   silence";
            Console.Write($"\r   level [{new string(bar)}] {state}   ");
            _meterVisible = true;
        }
    }

    /// <summary>Writes a markup line, erasing the meter first so output is never mangled.</summary>
    public static void Line(string markup)
    {
        lock (Gate)
        {
            EraseMeter();
            AnsiConsole.MarkupLine(markup);
        }
    }

    /// <summary>Renders a Spectre widget, erasing the meter first.</summary>
    public static void Render(IRenderable renderable)
    {
        lock (Gate)
        {
            EraseMeter();
            AnsiConsole.Write(renderable);
        }
    }

    public static void ClearMeter()
    {
        lock (Gate)
        {
            EraseMeter();
        }
    }

    private static void EraseMeter()
    {
        if (!_meterVisible)
        {
            return;
        }

        Console.Write('\r' + new string(' ', MeterWidth + 24) + '\r');
        _meterVisible = false;
        _lastLevelBlocks = -1;
    }
}

/// <summary>
/// Maps <see cref="DownloadProgress"/> reports onto a Spectre <see cref="ProgressTask"/>.
/// Byte counts are preferred over <c>PercentComplete</c> because they are authoritative,
/// and they let Spectre's <see cref="DownloadedColumn"/> show real transfer sizes.
/// </summary>
internal sealed class SpectreDownloadProgress : IProgress<DownloadProgress>
{
    private readonly ProgressTask _task;
    private readonly string _label;
    private readonly object _gate = new();

    public SpectreDownloadProgress(ProgressTask task, string label)
    {
        _task = task;
        _label = label;
    }

    public void Report(DownloadProgress value)
    {
        lock (_gate)
        {
            if (value.Stage == DownloadStage.Failed)
            {
                _task.Description =
                    $"[red]{Markup.Escape(_label)} — {Markup.Escape(value.Message ?? "download failed")}[/]";
                return;
            }

            if (value.TotalBytes > 0)
            {
                _task.MaxValue = value.TotalBytes;
                _task.Value = Math.Clamp(value.BytesDownloaded, 0, value.TotalBytes);
            }
            else
            {
                _task.MaxValue = 100;
                _task.Value = Math.Clamp(value.PercentComplete, 0, 100);
            }

            _task.Description = $"[deepskyblue1]{Markup.Escape(_label)}[/] {Markup.Escape(Describe(value))}";
        }
    }

    private static string Describe(DownloadProgress value)
    {
        var stage = value.Stage switch
        {
            DownloadStage.Checking => "checking",
            DownloadStage.Validating => "validating",
            DownloadStage.Complete => "complete",
            _ => Path.GetFileName(value.CurrentFile) is { Length: > 0 } name ? name : "downloading"
        };

        if (value.TotalFileCount > 0)
        {
            var index = Math.Clamp(value.CurrentFileIndex + 1, 1, value.TotalFileCount);
            stage += $" ({index}/{value.TotalFileCount})";
        }

        return stage;
    }
}
