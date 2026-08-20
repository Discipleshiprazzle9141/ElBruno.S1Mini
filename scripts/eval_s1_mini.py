#!/usr/bin/env python3
"""
Smoke-eval for converted superwhisper/s1-mini ONNX variants (int4 / fp16).

Loads each converted variant with onnxruntime_genai and runs a handful of
ASR-transcript-normalization prompts through the model's required system
prompt + control-line format, using greedy decoding (temperature 0,
do_sample=False), to sanity-check output quality before picking a default
KnownModels variant.

This is a throwaway diagnostic script (not part of the packaged library) —
run it manually after `convert_s1_mini.py` produces int4/ and fp16/ output
directories.

Usage:
    python scripts/eval_s1_mini.py --model-dir converted_models/s1-mini-onnx/int4
    python scripts/eval_s1_mini.py --model-dir converted_models/s1-mini-onnx/fp16
"""

import argparse
import sys
import time

SYSTEM_PROMPT = (
    "You are a text normalizer for speech-to-text transcripts. The input begins "
    "with a control line specifying the styling, structure, and context settings; "
    "clean the transcript to match those settings and output only the cleaned text."
)

TEST_CASES = [
    ("model-card reference",
     "[Styling: semi-formal] [Structure: prose] [Context: general]\n"
     "so um i need to like send the the report by uh friday no wait make that thursday"),
    ("email + phone",
     "[Styling: semi-formal] [Structure: prose] [Context: general]\n"
     "hey so uh my email is bruno at example dot com and i'll call you at like three thirty tomorrow"),
    ("pure filler (expect empty output)",
     "[Styling: semi-formal] [Structure: prose] [Context: general]\n"
     "um uh you know like"),
    ("longer dictation with self-correction",
     "[Styling: semi-formal] [Structure: prose] [Context: general]\n"
     "okay so uh the the meeting is at nine am no sorry it got moved to ten thirty and uh we need "
     "like twenty five copies of the the slide deck and uh don't forget to book room number two "
     "oh and uh bring the the laptop charger this time"),
    ("context: email",
     "[Styling: semi-formal] [Structure: prose] [Context: email]\n"
     "hi team uh just wanted to say the the deployment went fine last night and uh no issues so far"),
    ("structure: lists",
     "[Styling: semi-formal] [Structure: lists] [Context: general]\n"
     "so uh first we need to uh order the parts then uh schedule the install and uh finally uh "
     "test everything before uh shipping it out"),
]


def run_variant(model_dir: str) -> None:
    import onnxruntime_genai as og

    print(f"\n{'=' * 70}")
    print(f"  Loading model: {model_dir}")
    print(f"{'=' * 70}")

    t0 = time.time()
    model = og.Model(model_dir)
    tokenizer = og.Tokenizer(model)
    print(f"  Loaded in {time.time() - t0:.1f}s")

    for label, user_message in TEST_CASES:
        # enable_thinking=False, per the model's chat template, is signalled by
        # emitting an empty <think></think> block right after the assistant header.
        prompt = (
            f"<|im_start|>system\n{SYSTEM_PROMPT}<|im_end|>\n"
            f"<|im_start|>user\n{user_message}<|im_end|>\n"
            f"<|im_start|>assistant\n<think>\n\n</think>\n\n"
        )

        input_tokens = tokenizer.encode(prompt)

        # Greedy decoding: do_sample=False. temperature is intentionally omitted —
        # onnxruntime-genai divides logits by temperature even when do_sample=False,
        # so temperature=0.0 crashes the native runtime with an integer divide-by-zero.
        params = og.GeneratorParams(model)
        params.set_search_options(
            do_sample=False,
            max_length=len(input_tokens) + 1024,
        )

        generator = og.Generator(model, params)
        generator.append_tokens(input_tokens)

        t1 = time.time()
        while not generator.is_done():
            generator.generate_next_token()
        elapsed = time.time() - t1

        new_tokens = generator.get_sequence(0)[len(input_tokens):]
        # tokenizer.decode([]) crashes onnxruntime-genai's native decoder with an
        # integer divide-by-zero when the model emits EOS immediately (e.g. for
        # pure-filler input where the correct output is genuinely empty).
        output_text = tokenizer.decode(new_tokens) if len(new_tokens) > 0 else ""

        print(f"\n--- {label} ---")
        print(f"  Input : {user_message.splitlines()[-1]!r}")
        print(f"  Output: {output_text!r}")
        print(f"  ({elapsed:.1f}s, {len(new_tokens)} tokens)")


def main() -> None:
    parser = argparse.ArgumentParser(description="Smoke-eval s1-mini ONNX conversions.")
    parser.add_argument("--model-dir", required=True, help="Path to a converted variant (int4/ or fp16/ dir)")
    args = parser.parse_args()

    try:
        run_variant(args.model_dir)
    except Exception as e:
        print(f"\nERROR running eval on {args.model_dir}: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
