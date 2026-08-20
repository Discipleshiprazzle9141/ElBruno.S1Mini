#!/usr/bin/env python3
"""
Convert superwhisper/s1-mini to ONNX (INT4 / FP16) for ElBruno.LocalLLMs.

s1-mini is a 0.6B-parameter ASR-transcript normalizer fine-tuned from Qwen3-0.6B.
It is NOT a chat model: it takes a fixed system prompt plus a single control-line
user message and returns a cleaned transcript. This script converts it to ONNX
using onnxruntime-genai's model builder and optionally uploads the result to
elbruno/s1-mini-onnx on HuggingFace.

Usage:
    python convert_s1_mini.py
    python convert_s1_mini.py --output-dir ./my-output --skip-upload
    python convert_s1_mini.py --precision int4

Requirements:
    pip install onnxruntime-genai>=0.15.1 huggingface-hub[cli]>=0.24.0 transformers>=5.2.0 torch>=2.11.0 psutil>=5.9.0
"""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

try:
    import psutil
    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False

try:
    import torch
    HAS_TORCH = True
except ImportError:
    HAS_TORCH = False

# ── Constants ───────────────────────────────────────────────────────────────

SOURCE_MODEL_ID = "superwhisper/s1-mini"
TARGET_HF_REPO  = "elbruno/s1-mini-onnx"

# Disk space estimates for a 0.6B model
DISK_REQUIREMENTS = {
    "int4": {"download_gb": 2, "conversion_gb": 6, "output_gb": 0.5},
    "fp16": {"download_gb": 2, "conversion_gb": 6, "output_gb": 1.5},
}

REQUIRED_OUTPUT_FILES = [
    "model.onnx",
    "genai_config.json",
    "tokenizer.json",
    "tokenizer_config.json",
]

SYSTEM_PROMPT = (
    "You are a text normalizer for speech-to-text transcripts. The input begins "
    "with a control line specifying the styling, structure, and context settings; "
    "clean the transcript to match those settings and output only the cleaned text."
)

MODEL_CARD_TEMPLATE = """\
---
license: other
license_name: apache-2.0-with-naming-clause
license_link: https://huggingface.co/superwhisper/s1-mini/raw/main/LICENSE
base_model: superwhisper/s1-mini
tags:
  - onnx
  - onnxruntime-genai
  - qwen3
  - asr
  - text-normalization
---

# s1-mini ONNX ({precision_upper})

This repository contains an **unofficial** ONNX {precision_upper} conversion of
[superwhisper/s1-mini](https://huggingface.co/superwhisper/s1-mini)
for use with [ONNX Runtime GenAI](https://github.com/microsoft/onnxruntime-genai)
and the [ElBruno.LocalLLMs](https://github.com/elbruno/ElBruno.LocalLLMs) library.

This conversion is **not endorsed by or affiliated with Superwhisper**. All credit for
the underlying model goes to the Superwhisper team; see their model card and LICENSE
(included in this repository as `LICENSE`) for the original terms, including the
naming clause that accompanies the Apache-2.0 license.

## Model Description

s1-mini is a 596M-parameter (0.6B) speech-to-text transcript normalizer fine-tuned from
`Qwen/Qwen3-0.6B` (`Qwen3ForCausalLM`, 28 layers, GQA 16 Q heads / 8 KV heads, head_dim 128,
hidden_size 1024, vocab_size 151936, tied embeddings). It is **not a general-purpose chat
model** — it performs a single task: cleaning up raw, lowercase, unpunctuated ASR
transcripts into normalized text according to a control line.

## Required Prompt Format

System prompt (verbatim — required):

```text
{system_prompt}
```

User message format — a control line followed by the raw transcript:

```text
[Styling: semi-formal] [Structure: prose] [Context: general]
<raw lowercase unpunctuated asr transcript>
```

Supported control-line values include (non-exhaustive): `Styling: semi-formal|formal|casual`,
`Structure: prose|lists`, `Context: general|email|...`. The model outputs **only** the
cleaned transcript — no explanation, no preamble.

## Decoding

Use `enable_thinking=False` and **greedy decoding** (`do_sample=False`, `temperature=0`,
`max_new_tokens=1024`). Sampling is not validated for this model and may degrade output
quality/consistency.

> **Note on `enable_thinking=False`:** the model's chat template signals this by emitting
> an empty `<think>\n\n</think>\n\n` block immediately after the `<|im_start|>assistant\n`
> header. If you build prompts manually instead of through the chat template, include
> that block explicitly.

> **Note on `temperature`:** with `do_sample=False`, omit/avoid setting `temperature=0.0`
> explicitly in onnxruntime-genai's `GeneratorParams.set_search_options` — some
> onnxruntime-genai builds divide logits by `temperature` even in greedy mode, which
> crashes on a literal `0.0`. Leave `temperature` unset for greedy decoding.

## Known Issues

- **FP16 is currently broken on the CPU execution provider** with `onnxruntime-genai
  0.15.1`. Running the `fp16/` variant fails with a shape-mismatch error inside the
  ONNX Runtime buffer-reuse optimizer, in the GQA `repeat_kv` `Reshape` node
  (`InsertedPrecisionFreeCast_/model/layers.*/attn/v_proj/repeat_kv/Reshape_4`).
  **Use the `int4/` variant for CPU inference** until this is confirmed fixed in a
  newer `onnxruntime-genai` release. The `fp16/` artifact is still published here for
  future compatibility/GPU experimentation, but it is not currently validated to run.
- Calling `tokenizer.decode()` on an empty token sequence (which can legitimately
  happen — e.g. pure-filler input that should normalize to nothing) crashes the
  native `onnxruntime-genai` decoder with an integer divide-by-zero. Guard for a
  zero-length generated sequence in calling code and treat it as an empty string
  instead of calling `decode()`.

## Conversion Details

| Field | Value |
|---|---|
| Source | `superwhisper/s1-mini` |
| Precision | {precision_upper} |
| Execution provider | CPU (universal) |
| Tool | `onnxruntime_genai.models.builder` |
| Architecture | Qwen3-0.6B decoder-only |

## Usage with ElBruno.LocalLLMs

```csharp
using var client = await LocalChatClient.CreateAsync(new LocalLLMsOptions
{{
    Model = KnownModels.S1Mini,
    EnsureModelDownloaded = true   // downloads automatically on first run
}});

var response = await client.CompleteAsync(
    "[Styling: semi-formal] [Structure: prose] [Context: general]\\n" +
    "so um i need to like send the the report by uh friday no wait make that thursday");
```

## License

This conversion is distributed under the same terms as the source model: Apache-2.0
**with an additional naming clause** — see the included `LICENSE` file or
https://huggingface.co/superwhisper/s1-mini/raw/main/LICENSE for the authoritative text.
"""


# ── Preflight Checks ────────────────────────────────────────────────────────

def check_disk_space(output_dir: Path, precision: str) -> None:
    req = DISK_REQUIREMENTS.get(precision, DISK_REQUIREMENTS["int4"])
    peak_gb = req["download_gb"] + req["conversion_gb"]
    free_gb = shutil.disk_usage(output_dir.parent if output_dir.parent.exists() else Path(".")).free / (1024 ** 3)
    print(f"  Disk: {free_gb:.1f} GB free, {peak_gb} GB needed (download + conversion peak)")
    if free_gb < peak_gb:
        print(f"  WARNING: Only {free_gb:.1f} GB free, but {peak_gb} GB may be needed during conversion.")
        print("     Continuing anyway — conversion may fail if space runs out.")


def check_ram() -> None:
    if not HAS_PSUTIL:
        print("  RAM: psutil not installed — skipping RAM check")
        return
    ram_gb = psutil.virtual_memory().total / (1024 ** 3)
    print(f"  RAM: {ram_gb:.1f} GB total")
    if ram_gb < 8:
        print("  WARNING: Less than 8 GB RAM detected. Conversion may be slow for even a 0.6B model.")


def check_gpu() -> None:
    if HAS_TORCH and torch.cuda.is_available():
        name = torch.cuda.get_device_name(0)
        vram_gb = torch.cuda.get_device_properties(0).total_memory / (1024 ** 3)
        print(f"  GPU: {name} ({vram_gb:.1f} GB VRAM)")
        return
    result = subprocess.run(
        ["nvidia-smi", "--query-gpu=name,memory.total", "--format=csv,noheader"],
        capture_output=True, text=True
    )
    if result.returncode == 0 and result.stdout.strip():
        gpu_info = result.stdout.strip().split("\n")[0]
        print(f"  GPU: {gpu_info} (torch CPU-only build; conversion uses ORT-GenAI which has GPU support)")
    else:
        print("  GPU: No CUDA GPU detected. Conversion will run on CPU (fine for a 0.6B model).")


def check_hf_auth() -> None:
    result = subprocess.run(
        ["hf", "auth", "whoami"],
        capture_output=True, text=True
    )
    if result.returncode == 0:
        username = result.stdout.strip().split("\n")[0]
        print(f"  HuggingFace: authenticated as '{username}'")
    else:
        print("  HuggingFace: NOT authenticated")
        print("    Run `hf auth login` or set HF_TOKEN env var before uploading.")


def check_onnxruntime_genai() -> None:
    result = subprocess.run(
        [sys.executable, "-c", "import onnxruntime_genai; print(onnxruntime_genai.__version__)"],
        capture_output=True, text=True
    )
    if result.returncode == 0:
        print(f"  onnxruntime-genai: {result.stdout.strip()}")
    else:
        print("  onnxruntime-genai: NOT installed")
        print("    Run: pip install -U onnxruntime-genai")
        sys.exit(1)


def run_preflight(output_dir: Path, precisions: list[str], skip_upload: bool) -> None:
    print("\n── Preflight Checks ──────────────────────────────────────────────")
    check_onnxruntime_genai()
    check_ram()
    for precision in precisions:
        check_disk_space(output_dir, precision)
    check_gpu()
    if not skip_upload:
        check_hf_auth()
    print()


# ── Conversion ──────────────────────────────────────────────────────────────

def run_conversion(output_dir: Path, precision: str, cache_dir: Path) -> None:
    print(f"── Conversion ({precision}) ────────────────────────────────────────")
    print(f"  Source model : {SOURCE_MODEL_ID}")
    print(f"  Output dir   : {output_dir}")
    print(f"  Precision    : {precision}")
    print(f"  Cache dir    : {cache_dir}")
    print()
    print("  This will download ~1.2 GB of model weights and may take a few minutes.")
    print("  Do not interrupt the process once conversion starts.\n")

    output_dir.mkdir(parents=True, exist_ok=True)
    cache_dir.mkdir(parents=True, exist_ok=True)

    cmd = [
        sys.executable, "-m", "onnxruntime_genai.models.builder",
        "-m", SOURCE_MODEL_ID,
        "-o", str(output_dir),
        "-p", precision,
        "-e", "cpu",
        "--cache_dir", str(cache_dir),
    ]

    print(f"  Running: {' '.join(cmd)}\n")

    result = subprocess.run(cmd)
    if result.returncode != 0:
        print(f"\nConversion FAILED (exit code {result.returncode})")
        sys.exit(result.returncode)

    print(f"\nConversion completed for precision={precision}.")


# ── Output Validation ────────────────────────────────────────────────────────

def validate_output(output_dir: Path) -> None:
    print(f"\n── Output Validation ({output_dir.name}) ─────────────────────────")
    all_ok = True
    for fname in REQUIRED_OUTPUT_FILES:
        path = output_dir / fname
        if path.exists():
            size_mb = path.stat().st_size / (1024 ** 2)
            print(f"  OK    {fname} ({size_mb:.1f} MB)")
        else:
            print(f"  MISSING: {fname}")
            all_ok = False

    # Check for .onnx.data sidecar (large weight files)
    data_files = list(output_dir.glob("*.onnx.data"))
    for df in data_files:
        size_gb = df.stat().st_size / (1024 ** 3)
        print(f"  OK    {df.name} ({size_gb:.2f} GB)")

    if not all_ok:
        print("\nValidation FAILED — required output files are missing.")
        sys.exit(1)

    print("\nAll required output files present.")


# ── LICENSE fetch ────────────────────────────────────────────────────────────

def copy_upstream_license(dest_dir: Path) -> None:
    """Download the upstream LICENSE (Apache-2.0 + naming clause) into dest_dir."""
    license_url = "https://huggingface.co/superwhisper/s1-mini/raw/main/LICENSE"
    dest_path = dest_dir / "LICENSE"
    try:
        import urllib.request
        with urllib.request.urlopen(license_url, timeout=30) as resp:
            content = resp.read()
        dest_path.write_bytes(content)
        print(f"  LICENSE copied from {license_url}")
    except Exception as e:
        print(f"  WARNING: could not fetch upstream LICENSE ({e}). Writing pointer file instead.")
        dest_path.write_text(
            "See the authoritative LICENSE for superwhisper/s1-mini at:\n"
            f"{license_url}\n",
            encoding="utf-8",
        )


# ── HuggingFace Upload ───────────────────────────────────────────────────────

def upload_to_huggingface(output_dir: Path, precisions: list[str]) -> None:
    print(f"\n── Upload to HuggingFace ({TARGET_HF_REPO}) ──────────────────────")

    # Write top-level model card (references default variant = int4 if present)
    default_precision = "int4" if "int4" in precisions else precisions[0]
    readme_path = output_dir / "README.md"
    readme_path.write_text(
        MODEL_CARD_TEMPLATE.format(
            precision_upper=default_precision.upper(),
            system_prompt=SYSTEM_PROMPT,
        ),
        encoding="utf-8",
    )
    print("  README.md written.")

    copy_upstream_license(output_dir)

    try:
        from huggingface_hub import HfApi, create_repo
    except ImportError:
        print("huggingface-hub not installed. Run: pip install huggingface-hub[cli]>=0.24.0")
        sys.exit(1)

    api = HfApi()

    try:
        create_repo(
            repo_id=TARGET_HF_REPO,
            repo_type="model",
            exist_ok=True,
            private=False,
        )
        print(f"  Repo {TARGET_HF_REPO} ready.")
    except Exception as e:
        print(f"  WARNING: Could not create repo: {e}")

    print(f"  Uploading {output_dir} -> {TARGET_HF_REPO} ...")
    api.upload_folder(
        folder_path=str(output_dir),
        repo_id=TARGET_HF_REPO,
        repo_type="model",
        commit_message=f"Add ONNX conversion(s) of {SOURCE_MODEL_ID} ({', '.join(precisions)})",
    )
    print(f"\nUploaded to https://huggingface.co/{TARGET_HF_REPO}")


# ── Main ─────────────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Convert superwhisper/s1-mini to ONNX (int4/fp16) and publish to HuggingFace."
    )
    parser.add_argument(
        "--output-dir",
        default="./converted_models/s1-mini-onnx",
        help="Directory to write ONNX output files (default: ./converted_models/s1-mini-onnx). "
             "When --precision is 'both', int4/ and fp16/ subdirectories are created underneath.",
    )
    parser.add_argument(
        "--cache-dir",
        default="./cache_dir/s1-mini",
        help="HuggingFace cache directory for downloaded model weights",
    )
    parser.add_argument(
        "--precision",
        choices=["int4", "fp16", "both"],
        default="both",
        help="Quantization precision (default: both — builds int4/ and fp16/ subfolders)",
    )
    parser.add_argument(
        "--skip-upload",
        action="store_true",
        help="Convert only — do not upload to HuggingFace",
    )
    parser.add_argument(
        "--skip-conversion",
        action="store_true",
        help="Skip conversion (re-upload existing output-dir only)",
    )
    args = parser.parse_args()

    output_dir = Path(args.output_dir)
    cache_dir = Path(args.cache_dir)

    precisions = ["int4", "fp16"] if args.precision == "both" else [args.precision]

    print("=" * 68)
    print("   superwhisper/s1-mini -> ONNX Conversion for ElBruno.LocalLLMs")
    print("=" * 68)

    run_preflight(output_dir, precisions, args.skip_upload)

    variant_dirs: dict[str, Path] = {}
    for precision in precisions:
        variant_dir = output_dir / precision if args.precision == "both" else output_dir
        variant_dirs[precision] = variant_dir

        if not args.skip_conversion:
            run_conversion(variant_dir, precision, cache_dir)
            validate_output(variant_dir)
        else:
            print(f"── Conversion skipped for {precision} (--skip-conversion) ─────────")
            if variant_dir.exists() and any(variant_dir.iterdir()):
                validate_output(variant_dir)
            else:
                print("   (No output directory to validate)")

    if not args.skip_upload:
        upload_to_huggingface(output_dir, precisions)
    else:
        print("\n── Upload skipped (--skip-upload) ──────────────────────────────────")
        print(f"   Output is ready at: {output_dir.resolve()}")

    print("\nDone! Update KnownModels.S1Mini to:")
    print(f'   HuggingFaceRepoId = "{TARGET_HF_REPO}"')
    print('   ModelSubPath      = "int4"   (or "fp16")')
    print('   RequiredFiles     = ["int4/*"]   (or ["fp16/*"])')
    print('   HasNativeOnnx = true')


if __name__ == "__main__":
    main()
