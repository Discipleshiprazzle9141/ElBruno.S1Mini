# Scripts

Automation for ElBruno.S1Mini — both release helpers (PowerShell) and the model-conversion / evaluation utilities (Python).

## Release helpers (PowerShell)

Used by the `Publish to NuGet` workflow (`.github/workflows/publish.yml`) and available locally when preparing a release. Require PowerShell 7+ (`pwsh`).

- **`Set-ReleaseVersion.ps1`** — bumps the release version in every file that must stay in sync:
  1. `src/ElBruno.S1Mini/ElBruno.S1Mini.csproj` `<Version>`
  2. `README.md` `## What's New` section (prepends a new bullet, keeps exactly the last 5 entries)

  ```powershell
  ./scripts/Set-ReleaseVersion.ps1 -Version 0.2.0 `
      -Highlight '🚀 **`v0.2.0`** — Adds streaming NormalizeAsync overload.'
  ```

- **`Validate-ReleaseVersion.ps1`** — verifies that the csproj `<Version>` and the README `## What's New` section (exactly 5 bullets, first bullet mentions `v<Version>`) are consistent with the release version. Optionally validates packed assemblies.

  ```powershell
  ./scripts/Validate-ReleaseVersion.ps1 -Version 0.2.0
  ./scripts/Validate-ReleaseVersion.ps1 -Version 0.2.0 -PackageDirectory ./artifacts
  ```

- **`Validate-PackageAssemblyVersions.ps1`** — opens every `.nupkg` in a folder, reads the nuspec version, and confirms every embedded assembly's version matches. Run in CI after `dotnet pack` (see `publish.yml`).

  ```powershell
  ./scripts/Validate-PackageAssemblyVersions.ps1 -PackageDirectory ./artifacts
  ```

## Local test runner

- **`run-tests.ps1`** / **`run-tests.sh`** — convenience wrappers around `dotnet build` + `dotnet test` for the solution and the unit test project. Support `-SkipBuild`, `-SkipUnitTests`, `-Framework`, `-Filter`.

  ```powershell
  ./scripts/run-tests.ps1
  ./scripts/run-tests.ps1 -Filter "FullyQualifiedName~TranscriptNormalizerTests"
  ```

  ```bash
  ./scripts/run-tests.sh
  ./scripts/run-tests.sh --filter "FullyQualifiedName~TranscriptNormalizerTests"
  ```

## Model conversion (Python)

Python scripts for converting and evaluating the s1-mini ONNX model. Only needed by maintainers publishing new model artifacts to HuggingFace — the runtime C# library does not invoke them.

- **`convert_s1_mini.py`** — converts `superwhisper/s1-mini` (a 0.6B Qwen3 fine-tune) to ONNX using `onnxruntime-genai`'s model builder. Produces the `int4/` variant (INT4 quantization) and optionally `fp16/`. Publishes to `elbruno/s1-mini-onnx` on HuggingFace when `--skip-upload` is not set.
- **`eval_s1_mini.py`** — quick sanity-check evaluation of a converted s1-mini model against a small held-out set of transcript-normalization prompts. Uses `onnxruntime-genai` directly.

### Requirements

```bash
pip install onnxruntime-genai>=0.15.1 huggingface-hub[cli]>=0.24.0 transformers>=5.2.0 torch>=2.11.0 psutil>=5.9.0
```

### Usage

```bash
python convert_s1_mini.py --precision int4
python convert_s1_mini.py --output-dir ./my-output --skip-upload
python eval_s1_mini.py
```

### Notes

- The **INT4** variant is the only variant known to run correctly on CPU with `onnxruntime-genai` 0.15.1. FP16 hits an upstream ORT `repeat_kv` Reshape shape-mismatch bug in the GQA graph; the artifact is published to `elbruno/s1-mini-onnx/fp16/` for future runtime versions but should not be used today.
- These scripts are Python-only utilities for maintainer workflows.
