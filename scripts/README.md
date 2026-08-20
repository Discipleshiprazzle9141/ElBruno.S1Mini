# Scripts

Python scripts for converting and evaluating the s1-mini ONNX model.

## Files

- **`convert_s1_mini.py`** — converts `superwhisper/s1-mini` (a 0.6B Qwen3 fine-tune) to ONNX using `onnxruntime-genai`'s model builder. Produces the `int4/` variant (INT4 quantization) and optionally `fp16/`. Publishes to `elbruno/s1-mini-onnx` on HuggingFace when `--skip-upload` is not set.
- **`eval_s1_mini.py`** — quick sanity-check evaluation of a converted s1-mini model against a small held-out set of transcript-normalization prompts. Uses `onnxruntime-genai` directly.

## Requirements

```bash
pip install onnxruntime-genai>=0.15.1 huggingface-hub[cli]>=0.24.0 transformers>=5.2.0 torch>=2.11.0 psutil>=5.9.0
```

## Usage

```bash
python convert_s1_mini.py --precision int4
python convert_s1_mini.py --output-dir ./my-output --skip-upload
python eval_s1_mini.py
```

## Notes

- The **INT4** variant is the only variant known to run correctly on CPU with `onnxruntime-genai` 0.15.1. FP16 hits an upstream ORT `repeat_kv` Reshape shape-mismatch bug in the GQA graph; the artifact is published to `elbruno/s1-mini-onnx/fp16/` for future runtime versions but should not be used today.
- These scripts are Python-only utilities for maintainer workflows. The runtime C# library (`ElBruno.S1Mini`) does not need Python and does not invoke them.
