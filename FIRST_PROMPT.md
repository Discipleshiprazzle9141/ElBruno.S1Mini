# FIRST_PROMPT — ElBruno.S1Mini Bootstrap

> **⚠️ This file is temporary.** It is a one-shot bootstrap prompt for a coding agent (Copilot CLI / Squad coordinator) running inside the `C:\src\ElBruno.S1Mini` repo. **Delete this file once Phase 7 (First Release) has succeeded end-to-end.** Do not commit it to `main`. If you must commit it as a workspace pointer, do it on a scratch branch and delete before the `v0.1.0` tag.

---

## Who you are, agent

You are an autonomous coding agent with:

- Windows / PowerShell 7+ shell access
- `git`, `gh` (GitHub CLI, authenticated as `elbruno`), `dotnet` (SDK 8 + 10), `python` 3.10+, `pip`, `hf` (HuggingFace CLI)
- Read/write access to `C:\src\ElBruno.S1Mini\`
- Ability to run image-generation ("t2i") skills already installed on this machine

You will follow the phased plan below in order. **Each phase has a Verification gate — do not proceed to the next phase until the gate passes.** Where a step requires a human decision or a browser action Bruno must take, the step is marked **🛑 STOP-AND-ASK BRUNO** with the exact question and expected answer format.

Reply concisely between phases with a one-line status. Do not narrate every command.

---

## What this repo is (context — read once)

- **`ElBruno.S1Mini`** — a .NET NuGet library that normalizes raw ASR (speech-to-text) transcripts into clean written text.
- Wraps [`superwhisper/s1-mini`](https://huggingface.co/superwhisper/s1-mini) — a **0.6B Qwen3 fine-tune**, **not a chat model**. One job: transcript-in → cleaned-text-out.
- Runs locally via **ONNX Runtime GenAI 0.15.1** on CPU. Model auto-downloads from `elbruno/s1-mini-onnx` on HuggingFace (INT4 variant).
- Public API surface: `TranscriptNormalizer`, `S1MiniClient` (implements `Microsoft.Extensions.AI.IChatClient`), `S1MiniOptions`, DI extension `AddTranscriptNormalizer(...)`.
- English only, v1. Vendor claim: **94.8% token accuracy on 7,519 held-out cases** (Superwhisper's number — always attribute as vendor claim).
- Repo code license: **MIT**. Model weights: upstream **Apache-2.0 + naming clause**. Our ONNX conversion is explicitly unofficial / unaffiliated / non-endorsed.

### Non-negotiable technical invariants (do NOT regress)

Any change that contradicts these will break the library. Do not "improve" them.

1. **ORT-GenAI native crash at `temperature ≤ 0`.** Even with `do_sample=false`, calling `SetSearchOption("temperature", 0)` triggers a native integer divide-by-zero. The guard lives in `src/ElBruno.S1Mini/Internal/OnnxGenAIRuntime.cs` and is covered by `src/tests/ElBruno.S1Mini.Tests/Internal/OnnxGenAIRuntimeTemperatureTests.cs`. s1-mini runs greedy every call and hits this every call. **Never remove the guard.**
2. **Never batch-decode a zero-length token array.** `tokenizer.decode([])` crashes natively. Decoding is single-token/incremental by design.
3. **Empty output is CORRECT** for pure-filler input. Return `string.Empty`, do not throw.
4. **Prompt format is byte-exact**: Qwen3 ChatML ending in `<|im_start|>assistant\n<think>\n\n</think>\n\n`. Verified byte-for-byte against the model's `chat_template.jinja` and reproduced 6/6 outputs against the real model. Do not touch `Internal/Qwen3PromptBuilder.cs`.
5. **User message shape**: `[Styling: x] [Structure: y] [Context: z]\n{raw transcript}`. Greedy, `max_new_tokens=1024`, ~1000-token input recommendation.
6. **Empirically verified control values** (documented in README + `docs/transcript-normalization.md` — keep docs honest):
    - `Styling`: `semi-formal` (default), `formal`, `casual` — all three genuinely distinct.
    - `Structure`: `prose` (default), `lists` — **`lists` did NOT produce Markdown bullets** in any test run despite the model card implying it might. Kept for API completeness.
    - `Context`: `general` (default), `email` (genuinely distinct), plus `message` and `notes` which **behave IDENTICALLY to `general`** (verified twice).
7. **FP16 is BROKEN** on CPU with `onnxruntime-genai` 0.15.1 (upstream GQA `repeat_kv` Reshape shape-mismatch). INT4 is the sole working/default variant. FP16 is published under `fp16/` for future ORT-GenAI versions but is not validated to run today.
8. **Reference eval transcript** (used repeatedly below):
   `so um i need to like send the the report by uh friday no wait make that thursday`
   → should normalize to approximately: `I need to send the report by Thursday.`

### What already exists (do NOT recreate)

- `.slnx` solution, `Directory.Build.props`, `global.json`, `.gitignore`, `.editorconfig`, MIT `LICENSE`.
- Source: `src/ElBruno.S1Mini/`, tests: `src/tests/ElBruno.S1Mini.Tests/` (56 tests, passing), sample: `src/samples/HelloS1Mini/`.
- Workflows: `.github/workflows/build.yml` (CI), `.github/workflows/publish.yml` (OIDC trusted publishing to NuGet + What's-New-5-bullets validator).
- Docs: `README.md`, `docs/getting-started.md`, `docs/transcript-normalization.md`, `scripts/README.md`.
- Scripts: `scripts/convert_s1_mini.py`, `scripts/eval_s1_mini.py`, `scripts/Set-ReleaseVersion.ps1`, `scripts/Validate-ReleaseVersion.ps1`, `scripts/Validate-PackageAssemblyVersions.ps1`, `scripts/run-tests.ps1`, `scripts/run-tests.sh`.
- Squad + t2i skills already installed under `.squad/` and `.github/skills/`.
- ONE local git commit (`ea8be0a Initial scaffold: ElBruno.S1Mini standalone repo`) — **no remote**.
- The ONNX-converted model **already exists and is published** at [`elbruno/s1-mini-onnx`](https://huggingface.co/elbruno/s1-mini-onnx) with `int4/` + `fp16/` subfolders, model card, upstream `LICENSE`. **Do not re-convert-and-upload unless Phase 3 explicitly says to.**

### Known gaps this bootstrap closes

- `images/nuget_logo.png` is missing (the `<PackageIcon>` and `<None Include=...>` are `Condition="Exists(...)"` guarded, so builds stay green — but the package ships without an icon). → **Phase 1**
- No GitHub remote / repo. → **Phase 2**
- No web app sample (only a console sample exists). → **Phase 5**
- NuGet trusted-publishing not configured (`release` env, `NUGET_USER` secret, nuget.org trusted-publisher policy). → **Phase 6**
- No first release. → **Phase 7**

---

## Phase 0 — Preflight

**Goal:** Verify tooling, disk, auth, and baseline repo state.

### Steps

```powershell
cd C:\src\ElBruno.S1Mini

# .NET SDKs — need both 8.x and 10.x
dotnet --list-sdks

# GitHub auth — must be authenticated as elbruno
gh auth status

# Python + ORT-GenAI (optional here, required Phase 3)
python --version
pip show onnxruntime-genai

# HuggingFace auth (required only if Phase 3 re-uploads)
hf auth whoami

# Repo state
git log --oneline -5
git remote -v            # expect: (empty)
git status               # expect: nothing to commit, working tree clean

# Disk (need ~10 GB free if you reproduce Phase 3 conversion locally)
Get-PSDrive C | Select-Object Used, Free
```

### Baseline build + test

```powershell
dotnet restore ElBruno.S1Mini.slnx
dotnet build   ElBruno.S1Mini.slnx --no-restore -c Release
dotnet test    ElBruno.S1Mini.slnx --no-build   -c Release --framework net8.0
```

### Verification gate

- `dotnet --list-sdks` shows at least an `8.0.x` SDK. `10.0.x` optional locally (CI has it).
- `gh auth status` shows logged in as `elbruno`.
- `git remote -v` is empty. `git log` shows exactly one commit (`ea8be0a`).
- Build: **0 warnings, 0 errors**.
- Tests: **56 passed / 0 failed** on `net8.0`.

If any check fails, **🛑 STOP-AND-ASK BRUNO** with the exact failing command and its output before continuing.

---

## Phase 1 — NuGet Icon Asset

**Goal:** Generate `images/nuget_logo.png` and confirm `dotnet pack` embeds it (no NU5046 warning).

### Steps

1. **Reference visual style.** Bruno's ecosystem uses a consistent icon look. Reference file (verified exists):
   `C:\src\ElBruno.LocalLLMs\images\nuget_logo.png`
   Open it and match its visual language: dark background, rounded square, single glyph/mark, orange/blue accent typical of ElBruno's brand. Look at sibling repos (`ElBruno.Whisper`, `ElBruno.QwenTTS`) in `C:\src\` if their `images/nuget_logo.png` is present — imitate the family look, don't copy the same glyph.

2. **Generate via t2i skill.** Suggested prompt (adapt to whichever t2i skill is installed):
   > *"Square NuGet package icon, 512×512, dark navy background with a subtle radial gradient, single centered glyph combining a stylized speech-waveform arc morphing into a clean text line (representing raw speech being cleaned into written text). Orange accent (#FF8C42) on the waveform side, soft cyan (#4EC5F1) on the clean-text side. Flat, modern, high contrast, no text, no letters, no logos, no watermarks. Matches the visual family of ElBruno's other .NET library icons."*

3. Save output to `C:\src\ElBruno.S1Mini\images\nuget_logo.png`. Confirm 512×512 PNG, < 200 KB.

4. Verify pack embeds it:

    ```powershell
    dotnet pack src\ElBruno.S1Mini\ElBruno.S1Mini.csproj -c Release -o .\artifacts-preflight
    # Inspect the nupkg
    $nupkg = Get-ChildItem .\artifacts-preflight\*.nupkg | Select-Object -First 1
    Expand-Archive -Path $nupkg.FullName -DestinationPath .\artifacts-preflight\extracted -Force
    Test-Path .\artifacts-preflight\extracted\nuget_logo.png    # expect: True
    # Clean up
    Remove-Item -Recurse -Force .\artifacts-preflight
    ```

### Verification gate

- `images/nuget_logo.png` exists, 512×512, < 200 KB.
- `dotnet pack` prints **no NU5046** warning ("NuGet package icon file is missing").
- Extracted nupkg root contains `nuget_logo.png`.

### Stop-and-ask flags

- If the generated icon looks off-brand or unprofessional, **🛑 STOP-AND-ASK BRUNO**: *"Icon draft at `images/nuget_logo.png` — approve or ask for a re-roll?"*

---

## Phase 2 — GitHub Repository

**Goal:** Create the public repo at `github.com/elbruno/ElBruno.S1Mini`, push `main`, confirm CI green on first push.

### Steps

```powershell
cd C:\src\ElBruno.S1Mini

# Stage the icon (only unstaged change expected from Phase 1)
git status
git add images/nuget_logo.png
git commit -m "Add NuGet package icon" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"

# Create the PUBLIC repo — do NOT auto-push yet; we push explicitly below to control the branch name
gh repo create elbruno/ElBruno.S1Mini `
  --public `
  --description "Local ASR transcript normalizer for .NET — wraps superwhisper/s1-mini (0.6B Qwen3) via ONNX Runtime GenAI." `
  --homepage "https://www.nuget.org/packages/ElBruno.S1Mini" `
  --source . `
  --remote origin

# Topics
gh repo edit elbruno/ElBruno.S1Mini --add-topic dotnet,csharp,nuget,asr,speech-to-text,transcript,onnx,onnxruntime-genai,qwen3,huggingface,local-llm,microsoft-extensions-ai

# Ensure local branch is main (it should already be)
git branch -M main
git push -u origin main
```

### Verification gate

```powershell
# CI on first push must go green
gh run list --repo elbruno/ElBruno.S1Mini --workflow build.yml --limit 1
gh run watch --repo elbruno/ElBruno.S1Mini
```

- Repo exists at https://github.com/elbruno/ElBruno.S1Mini, visibility **public**.
- `main` pushed, CI `build.yml` completes **successfully** on the first run.
- README badges render (may show "no releases yet" / "no NuGet version" — that's expected until Phase 7).

### Stop-and-ask flags

- If `gh repo create` fails because the repo already exists, **🛑 STOP-AND-ASK BRUNO** — do not `--force` push or delete. Ask whether to reuse or pick a different name.
- If CI fails on first push, halt and diagnose. Do not proceed.

---

## Phase 3 — Model Verification (do NOT re-upload)

**Goal:** Confirm `elbruno/s1-mini-onnx` on HuggingFace is intact. Optionally reproduce conversion locally with `--skip-upload` for smoke-testing. Then run the eval script against the reference transcript.

The model is **already published**. This phase is verification, not fresh work.

### Steps

1. **Verify HF repo integrity (read-only):**

    ```powershell
    # Requires: pip install "huggingface-hub[cli]>=0.24.0"
    hf repo files elbruno/s1-mini-onnx
    # Expect at minimum:
    #   README.md
    #   LICENSE
    #   int4/model.onnx
    #   int4/model.onnx.data
    #   int4/genai_config.json
    #   int4/tokenizer.json
    #   int4/tokenizer_config.json
    #   fp16/... (same file set)
    ```

    Confirm the model card at https://huggingface.co/elbruno/s1-mini-onnx renders and mentions the FP16-broken caveat.

2. **(Optional) Reproduce conversion locally** — for smoke-testing only, **never** upload:

    ```powershell
    pip install -U "onnxruntime-genai>=0.15.1" "huggingface-hub[cli]>=0.24.0" "transformers>=5.2.0" "torch>=2.11.0" "psutil>=5.9.0"

    # INT4 only, no upload — takes a few minutes, needs ~8 GB free
    python scripts\convert_s1_mini.py --precision int4 --skip-upload --output-dir .\converted_models\s1-mini-onnx
    ```

    Validates: preflight checks pass; `int4/model.onnx`, `int4/genai_config.json`, `int4/tokenizer.json`, `int4/tokenizer_config.json` produced.

3. **Run eval against the reference transcript:**

    ```powershell
    # eval_s1_mini.py takes --model-dir pointing to a converted variant folder
    python scripts\eval_s1_mini.py --model-dir .\converted_models\s1-mini-onnx\int4
    ```

    Confirm the reference transcript
    `so um i need to like send the the report by uh friday no wait make that thursday`
    normalizes to approximately `I need to send the report by Thursday.` (exact punctuation may vary; the meaning and the Friday→Thursday self-correction must be resolved).

### Verification gate

- HF repo `elbruno/s1-mini-onnx` file list matches expectations, `int4/` variant is complete.
- If optional local reconversion was run, `convert_s1_mini.py` exited 0 and validation printed "All required output files present."
- Eval script produced a reasonable normalization for the reference transcript.

### Stop-and-ask flags

- If the HF repo is missing `int4/` or the model card, **🛑 STOP-AND-ASK BRUNO** before touching it: *"HF repo `elbruno/s1-mini-onnx` appears incomplete — should I re-upload from a local conversion? (This would overwrite a currently-working published artifact.)"*
- **Do not run `convert_s1_mini.py` without `--skip-upload`** unless Bruno explicitly authorizes it in writing. The published artifact is what every existing `S1MiniClient.CreateAsync()` call downloads.

---

## Phase 4 — C# Code + Test Review

**Goal:** Confirm the public API surface is coherent, invariants are preserved, docs match behavior, and all tests pass.

### Steps

```powershell
cd C:\src\ElBruno.S1Mini

# Full build + full test run on both targets
dotnet build ElBruno.S1Mini.slnx -c Release
dotnet test  ElBruno.S1Mini.slnx -c Release --framework net8.0 --verbosity normal
```

Then review:

1. **Public API surface** — inventory public types under `src/ElBruno.S1Mini/`:
    - `S1MiniClient` — implements `IChatClient`
    - `S1MiniOptions`, `S1MiniServiceExtensions`
    - `Normalization/TranscriptNormalizer`, `TranscriptNormalizerOptions`, `TranscriptContext`, `TranscriptStructure`, `TranscriptStyling`
   Confirm nothing else is `public` that shouldn't be. `Internal/` should stay internal.

2. **Temperature-0 guard present:**

    ```powershell
    Select-String -Path src\ElBruno.S1Mini\Internal\OnnxGenAIRuntime.cs -Pattern "temperature"
    Select-String -Path src\tests\ElBruno.S1Mini.Tests\Internal\OnnxGenAIRuntimeTemperatureTests.cs -Pattern "temperature|Fact|Theory"
    ```

    Confirm the guard exists in runtime and the tests exercise the ≤0 path.

3. **Docs consistency check:** open `README.md`, `docs/getting-started.md`, `docs/transcript-normalization.md`. Verify every one of these caveats is stated at least once:
    - `Structure.Lists` does **not** reliably produce Markdown bullets.
    - `Context.Message` and `Context.Notes` behave **identically to `General`**.
    - FP16 is **broken on CPU** with `onnxruntime-genai 0.15.1`; INT4 is the sole working variant.
    - License: MIT for code; upstream Apache-2.0 + naming clause for model weights; conversion is unofficial/unaffiliated/non-endorsed.
    - 94.8% accuracy is a **vendor claim**, not our measurement.
    - English only, v1.

4. **XML doc comments** — confirm `TranscriptStyling`, `TranscriptStructure`, `TranscriptContext` enum members carry XML doc comments that reflect the empirical caveats.

### Verification gate

- Build clean on `net8.0` and `net10.0`.
- All tests pass (should be 56 or higher — Phase 5 may add more).
- Every doc caveat above is present in at least one of the three doc files.
- Nothing public that shouldn't be.

### Stop-and-ask flags

- If any doc file makes a claim contradicting the invariants list at the top of this file, **🛑 STOP-AND-ASK BRUNO**: quote the offending sentence and ask whether to correct it before release. (Do not silently rewrite documented invariants.)

---

## Phase 5 — Web App Sample

**Goal:** Create a minimal web sample demonstrating `TranscriptNormalizer` — textarea → *Normalize* button → cleaned output, with dropdowns for `Styling`, `Structure`, `Context`. Wire it into the solution, run it, verify end-to-end normalization against the reference transcript, add a screenshot.

Bruno's ask: *"have or create a sample web app"*. The obvious shape is a **Blazor Server** app (single-project, no separate JS build, matches the style of other ElBruno samples). A minimal-API + static HTML page is acceptable too — pick Blazor Server for consistency unless there's a specific reason not to.

### Steps

1. **Scaffold the project:**

    ```powershell
    cd C:\src\ElBruno.S1Mini
    dotnet new blazorserver -n S1MiniWebSample -o src\samples\S1MiniWebSample --framework net8.0
    ```

    Then edit `src\samples\S1MiniWebSample\S1MiniWebSample.csproj`:
    - Add `<ProjectReference Include="..\..\ElBruno.S1Mini\ElBruno.S1Mini.csproj" />`.
    - Set `<IsPackable>false</IsPackable>`.
    - Keep `TargetFramework` at `net8.0` only (do not multi-target samples).

2. **Wire DI** in `Program.cs`:

    ```csharp
    using ElBruno.S1Mini;
    builder.Services.AddTranscriptNormalizer(options =>
    {
        // Reuse an already-downloaded model if set, otherwise auto-download on first request
        var localPath = Environment.GetEnvironmentVariable("S1MINI_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            options.ModelPath = localPath;
            options.EnsureModelDownloaded = false;
        }
    });
    ```

3. **UI (single Razor page, e.g. `Pages/Normalize.razor` set as `@page "/"`):**
    - `<textarea>` for the raw transcript (rows 8+, monospace).
    - Three `<select>` dropdowns bound to `TranscriptStyling`, `TranscriptStructure`, `TranscriptContext` enums (with caveat tooltips: "Lists rarely produces bullets", "Message / Notes behave like General").
    - "Normalize" button — disables + shows spinner while the first request is downloading the model (this can take a while on first run).
    - Output `<pre>` for the cleaned result.
    - Small footer noting: *English only. INT4 CPU inference. Vendor accuracy claim: 94.8% (Superwhisper).*
    - Prefill the textarea with the reference transcript on load so the demo works with one click.

4. **Register in the solution:**

    Edit `ElBruno.S1Mini.slnx` — add under the existing `/src/samples/` folder:

    ```xml
    <Project Path="src\samples\S1MiniWebSample\S1MiniWebSample.csproj" />
    ```

5. **Build + smoke-run:**

    ```powershell
    dotnet build ElBruno.S1Mini.slnx -c Release
    dotnet run --project src\samples\S1MiniWebSample\S1MiniWebSample.csproj -c Release
    ```

    Browse to the printed localhost URL, paste the reference transcript, click Normalize, confirm output is roughly `I need to send the report by Thursday.`

6. **Screenshot:** save `docs/images/websample.png` (create the directory if needed). Reference it from `README.md` under **Samples** and from `docs/getting-started.md`.

7. **README + docs update:**
    - Add a row to the **Samples** table in `README.md`:
      `| [S1MiniWebSample](src/samples/S1MiniWebSample) | Blazor Server web UI: textarea → Normalize → cleaned output with styling/structure/context selectors. |`
    - Add a short "Web sample" section in `docs/getting-started.md` with the run command and the screenshot.

### Verification gate

- Solution still builds clean (`dotnet build ElBruno.S1Mini.slnx -c Release`) — **0 warnings, 0 errors**.
- All existing tests still pass.
- Web sample runs, serves the page, and produces a plausible normalization for the reference transcript against the real model (not a fake).
- Screenshot committed at `docs/images/websample.png`.
- Solution file (`ElBruno.S1Mini.slnx`) lists the new project under `/src/samples/`.

### Stop-and-ask flags

- If the model fails to auto-download inside the web app (HF rate-limit, offline, disk full), **🛑 STOP-AND-ASK BRUNO** rather than committing a broken sample.
- **Optional / do NOT do in this bootstrap:** several sibling ElBruno repos ship a `.BlazorComponents` RCL package (`ElBruno.Whisper.BlazorComponents`, `ElBruno.Speech.BlazorComponents`). If Bruno wants that pattern for S1Mini, it's a separate follow-up package — do not carve it out here. Flag this for Phase 8 as an open question.

### Commit + push

```powershell
git add src\samples\S1MiniWebSample\ ElBruno.S1Mini.slnx README.md docs\
git commit -m "Add S1MiniWebSample: Blazor Server sample for transcript normalization" `
           -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
git push
gh run watch --repo elbruno/ElBruno.S1Mini
```

CI must go green after this push.

---

## Phase 6 — NuGet Trusted-Publishing Setup

**Goal:** Configure NuGet OIDC trusted publishing so `publish.yml` can push without any long-lived API key. This is **90% manual browser work by Bruno.**

`publish.yml` is already correctly wired: uses `NuGet/login@v1` with `user: ${{ secrets.NUGET_USER }}`, has `permissions: id-token: write`, runs under `environment: release`. It just needs the three pieces below.

### Steps

#### 6.1 — Create the GitHub `release` environment (agent can do this)

```powershell
# Create environment (idempotent — 204 or 200)
gh api --method PUT repos/elbruno/ElBruno.S1Mini/environments/release --silent
gh api repos/elbruno/ElBruno.S1Mini/environments/release
```

Optionally add environment protection rules (recommended — required reviewers = Bruno). This must be done in the browser under **Settings → Environments → release**:
- ✅ Required reviewers: `elbruno`
- ✅ Restrict to `main` branch (Deployment branches: Selected branches → `main`)

#### 6.2 — 🛑 STOP-AND-ASK BRUNO: `NUGET_USER` secret

**Ask Bruno exactly this:**
> "I'm about to configure NuGet trusted publishing. Two questions:
> 1. What is your **nuget.org username** (case-sensitive — this becomes the `NUGET_USER` secret)?
> 2. Do you want me to set it via `gh secret set NUGET_USER --env release --body <username> --repo elbruno/ElBruno.S1Mini`, or will you set it yourself in the browser?"

Once Bruno answers, set it in the `release` environment (not the repo-wide secrets):

```powershell
# Only if Bruno approves the CLI path
gh secret set NUGET_USER --env release --repo elbruno/ElBruno.S1Mini
# (paste the username at the prompt — do not put it on the command line)
```

Confirm:

```powershell
gh secret list --env release --repo elbruno/ElBruno.S1Mini
# Expect: NUGET_USER
```

#### 6.3 — 🛑 STOP-AND-ASK BRUNO: nuget.org trusted-publisher policy (browser only)

The agent **cannot do this step.** Instructions for Bruno, verbatim:

> **On https://www.nuget.org:**
> 1. Sign in.
> 2. Go to your account → **API Keys** → **Trusted Publishing** (or **Manage Trusted Publishers**).
> 3. Click **Add** → **GitHub Actions**.
> 4. Fill in:
>    - **Package Owner**: your nuget.org username (same as `NUGET_USER`)
>    - **Repository Owner**: `elbruno`
>    - **Repository**: `ElBruno.S1Mini`
>    - **Workflow filename**: `publish.yml`
>    - **Environment**: `release`
>    - **Package Owners scope**: `ElBruno.S1Mini` (glob — matches this package and future symbol packages)
> 5. Save.
>
> Then reply here: **"trusted publisher configured"** so I can continue.

Do not proceed until Bruno confirms.

### Verification gate

- GitHub environment `release` exists.
- `NUGET_USER` secret set on that environment.
- Bruno confirms trusted-publisher policy is live on nuget.org.

There is no non-destructive way to smoke-test OIDC ahead of a real release — Phase 7 is the actual gate.

---

## Phase 7 — First Release: `v0.1.0`

**Goal:** Cut `v0.1.0`, watch `publish.yml` succeed end-to-end, verify the package is live on nuget.org and installable from a clean scratch project.

### Steps

1. **Check the What's New section is release-ready** — `publish.yml` fails if `## What's New` in `README.md` does not have **exactly 5 bullets** starting with `- `.

    ```powershell
    # Manual visual check
    (Get-Content README.md -Raw) -match '(?s)## What''s New(.*?)(?=^## )'
    ```

    If the current top bullet already mentions `v0.1.0`, you can skip step 2. Otherwise:

2. **(Only if needed) Bump the version and add a What's New entry:**

    The csproj already has `<Version>0.1.0</Version>` — no bump needed for the first release. But if Phase 5 or Phase 4 changed anything user-visible, use the release helper to keep exactly 5 bullets:

    ```powershell
    ./scripts/Set-ReleaseVersion.ps1 -Version 0.1.0 `
        -Highlight '🎉 **`v0.1.0`** — Initial release: TranscriptNormalizer with styling/structure/context, chunking helper, DI extension, and Blazor Server sample.'

    ./scripts/Validate-ReleaseVersion.ps1 -Version 0.1.0
    ```

3. **Commit and push any release-prep changes:**

    ```powershell
    git status
    git add -A
    git commit -m "Prep v0.1.0 release" `
               -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
    git push
    ```

4. **Create the GitHub release** — this triggers `publish.yml`:

    ```powershell
    gh release create v0.1.0 `
      --repo elbruno/ElBruno.S1Mini `
      --title "v0.1.0 — Initial release" `
      --notes "Initial release of ElBruno.S1Mini — local ASR transcript normalizer wrapping superwhisper/s1-mini via ONNX Runtime GenAI. See README for API surface and control-line reference. English only, v1."
    ```

    If the `release` environment has required-reviewers protection, the workflow will pause pending Bruno's approval — **🛑 STOP-AND-ASK BRUNO**: *"`publish.yml` is paused pending your approval on the `release` environment — approve at https://github.com/elbruno/ElBruno.S1Mini/actions and reply when done."*

5. **Watch it:**

    ```powershell
    gh run watch --repo elbruno/ElBruno.S1Mini
    ```

    All steps must pass:
    - Restore / Build / Unit Tests
    - **Validate README What's New policy** (fails if ≠ 5 bullets)
    - Pack
    - **Validate packed assembly versions**
    - NuGet login (OIDC → temp API key) — fails if trusted-publisher policy is missing/misconfigured
    - Push to NuGet.org
    - Upload NuGet package artifact

6. **Verify on nuget.org.** May take 5–15 minutes for indexing:

    ```powershell
    # Poll until it appears (up to ~15 min)
    Start-Sleep -Seconds 60
    dotnet package search ElBruno.S1Mini --source https://api.nuget.org/v3/index.json --exact-match
    ```

7. **Smoke-test installation from a scratch project** — this is the actual acceptance gate:

    ```powershell
    $scratch = "C:\src\_s1mini-smoke"
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $scratch | Out-Null
    Push-Location $scratch
    dotnet new console -n S1MiniSmoke --framework net8.0
    Set-Location S1MiniSmoke
    dotnet add package ElBruno.S1Mini --version 0.1.0
    # Replace Program.cs with a 5-line normalize call — reuse the README Quick Start
    @'
using ElBruno.S1Mini.Normalization;
using var normalizer = await TranscriptNormalizer.CreateAsync();
var cleaned = await normalizer.NormalizeAsync(
    "so um i need to like send the the report by uh friday no wait make that thursday");
System.Console.WriteLine(cleaned);
'@ | Set-Content Program.cs
    dotnet run -c Release
    Pop-Location
    ```

    Expected: prints something close to `I need to send the report by Thursday.`

### Verification gate

- `publish.yml` run status: **success**.
- Package visible at https://www.nuget.org/packages/ElBruno.S1Mini/0.1.0 with the icon rendered.
- Symbols package `.snupkg` present.
- Scratch project installs the package and successfully normalizes the reference transcript.

### Stop-and-ask flags

- If OIDC login fails (`Error: unable to exchange OIDC token`): trusted-publisher policy on nuget.org is misconfigured. Compare against Phase 6.3 exactly, then **🛑 STOP-AND-ASK BRUNO** to re-check the browser settings.
- If What's-New validator fails: reply with the exact bullet count and ask Bruno to approve either trimming or the addition of a new bullet before re-running.

---

## Phase 8 — Post-launch (optional)

Do these only if Bruno asks.

- Add S1Mini to the cross-repo "Related Projects" sections of `ElBruno.LocalLLMs`, `ElBruno.Whisper`, `ElBruno.HuggingFace` (open PRs against each).
- Draft an announcement post (blog / YouTube / LinkedIn) — surface Bruno's blog stub, don't publish it.
- **Decision point:** should S1Mini get a sibling `ElBruno.S1Mini.BlazorComponents` RCL package like Whisper/Speech? Bring the question to Bruno as a Morpheus decision brief.
- Re-test FP16 against a newer `onnxruntime-genai` (e.g. 0.16.x when released). If it works, update `README.md` and `docs/transcript-normalization.md` and remove the "broken" caveat.
- **DELETE `FIRST_PROMPT.md`** and commit that deletion. This file has served its purpose.

---

## Rollback & Troubleshooting

### NU5046 — "The 'PackageIcon' file 'nuget_logo.png' was not found"
Phase 1 didn't finish. The `Condition="Exists(...)"` guards in `Directory.Build.props` and the csproj mean the pack should still succeed *without* the icon — but if you removed the guard by mistake, restore it or generate the icon.

### OIDC / NuGet login failure in `publish.yml`
- Confirm the workflow is running in the `release` environment (job header should say so).
- Confirm `NUGET_USER` secret is set **on the environment**, not on the repo.
- Confirm the nuget.org trusted-publisher policy has the right Repository Owner (`elbruno`), Repository (`ElBruno.S1Mini`), Workflow (`publish.yml`), and Environment (`release`) — all four must match exactly.
- Case sensitivity matters for `NUGET_USER`.

### "What's New" validator failed
Section must have **exactly 5 bullets** each starting with `- ` between `## What's New` and the next `## `. Use `scripts/Set-ReleaseVersion.ps1 -Highlight ...` — it keeps the count at 5 automatically.

### ORT-GenAI native crash on first inference
Almost always one of:
1. Temperature-0 guard was removed → restore `Internal/OnnxGenAIRuntime.cs`.
2. Empty token decode → confirm the incremental decoding path, not batch `decode([])`.
3. Trying to run the FP16 variant on CPU → don't; use INT4.

### HuggingFace download fails at runtime
- Rate limit → set `HF_TOKEN` env var and re-run.
- Offline / firewall → set `S1MINI_MODEL_PATH` to a pre-downloaded model directory (see `HelloS1Mini/Program.cs` for the pattern) and `options.EnsureModelDownloaded = false`.

### Git push rejected (non-fast-forward)
Do **not** `--force`. Fetch, rebase locally, resolve, push again. If unclear, **🛑 STOP-AND-ASK BRUNO**.

### Solution file (`ElBruno.S1Mini.slnx`) — hand-edit only
It is XML, not the classic `.sln` format. Add `<Project Path="..." />` entries under the appropriate `<Folder>`. `dotnet sln` does not (yet) fully support `.slnx` add/remove — edit the XML by hand.

---

## Final self-check before you delete this file

- [ ] Phase 0 baseline clean.
- [ ] `images/nuget_logo.png` committed, in the nupkg.
- [ ] Public repo live at github.com/elbruno/ElBruno.S1Mini, CI green.
- [ ] HF model `elbruno/s1-mini-onnx` intact, eval passes.
- [ ] Full test suite green on both `net8.0` and `net10.0`.
- [ ] Web sample runs and normalizes the reference transcript against the real model.
- [ ] `release` environment + `NUGET_USER` secret set; nuget.org trusted publisher configured (confirmed by Bruno).
- [ ] `v0.1.0` GitHub release cut; `publish.yml` succeeded; package live on nuget.org with icon.
- [ ] `dotnet add package ElBruno.S1Mini` works in a clean scratch project and normalizes the reference transcript.
- [ ] Delete `FIRST_PROMPT.md` and commit the deletion.

Done. 🧡
