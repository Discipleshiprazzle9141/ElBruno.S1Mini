using ElBruno.S1Mini;
using ElBruno.S1Mini.Normalization;

// ─────────────────────────────────────────────────────────────────────────────
// s1-mini — ASR transcript normalizer (superwhisper/s1-mini), running locally
// through ONNX Runtime GenAI.
//
// This is NOT a chat model. It performs one job: rewriting a raw speech-to-text
// transcript into clean written text — fillers removed, self-corrections
// resolved to what the speaker landed on, punctuation/capitalization applied,
// spoken numbers/dates/times/currency/emails rendered in written form.
//
// Set S1MINI_MODEL_PATH to reuse an already-downloaded model directory.
// ─────────────────────────────────────────────────────────────────────────────

var options = new S1MiniOptions();

var localPath = Environment.GetEnvironmentVariable("S1MINI_MODEL_PATH");
if (!string.IsNullOrWhiteSpace(localPath))
{
    options.ModelPath = localPath;
    options.EnsureModelDownloaded = false;
}

Console.WriteLine("Loading s1-mini (this can take a while on first run)...");
using var normalizer = await TranscriptNormalizer.CreateAsync(options);
Console.WriteLine("Ready.\n");

// ── 1. Default normalization (semi-formal / prose / general) ────────────────

const string headline = "so um i need to like send the the report by uh friday no wait make that thursday";

Console.WriteLine("── Default (semi-formal / prose / general) ──");
Console.WriteLine($"Raw:     {headline}");
var cleaned = await normalizer.NormalizeAsync(headline);
Console.WriteLine($"Cleaned: {cleaned}");
Console.WriteLine();

// ── 2. Context: email ────────────────────────────────────────────────────────

const string emailTranscript =
    "hey sarah it's uh mike here just wanted to let you know that um the the budget numbers " +
    "are ready i'll send them over by end of day thanks talk soon";

Console.WriteLine("── Context: email ──");
Console.WriteLine($"Raw:     {emailTranscript}");
var emailCleaned = await normalizer.NormalizeAsync(
    emailTranscript,
    new TranscriptNormalizerOptions { Context = TranscriptContext.Email });
Console.WriteLine($"Cleaned:\n{emailCleaned}");
Console.WriteLine();

// ── 3. Structure: lists ──────────────────────────────────────────────────────

const string listTranscript =
    "okay so first we need to uh finalize the budget then second we have to like schedule " +
    "the kickoff meeting and uh third don't forget to send the invites";

Console.WriteLine("── Structure: lists ──");
Console.WriteLine($"Raw:     {listTranscript}");
var listCleaned = await normalizer.NormalizeAsync(
    listTranscript,
    new TranscriptNormalizerOptions { Structure = TranscriptStructure.Lists });
Console.WriteLine($"Cleaned:\n{listCleaned}");
Console.WriteLine();

// ── 4. Pure filler/noise → empty output ──────────────────────────────────────

const string fillerOnly = "um uh so like yeah um";

Console.WriteLine("── Pure filler/noise ──");
Console.WriteLine($"Raw:     {fillerOnly}");
var fillerCleaned = await normalizer.NormalizeAsync(fillerOnly);
Console.WriteLine($"Cleaned: \"{fillerCleaned}\" (expected: empty string)");
