# Social copy for `s1-mini-local-transcript-cleanup-dotnet.md`

## LinkedIn

`you don't have any any any any change at all?` -> `You don't have any change at all.`

That is the kind of cleanup I wanted from `ElBruno.S1Mini`, a .NET library for running `superwhisper/s1-mini` locally with ONNX Runtime GenAI.

s1-mini is not a chat model and not a speech model. It takes an existing raw ASR transcript and normalizes it into clean written text: filler removal, stutter collapse, self-correction resolution, punctuation, capitalization, numbers, dates, times, and currency formatting.

The new blog post covers:

- a copy-pasteable C# sample
- the live microphone pipeline: NAudio -> Silero VAD -> Whisper -> s1-mini
- why energy-threshold VAD accidentally removed the fillers before the model could clean them
- the ONNX migration details, including why INT4 is the default and FP16 is currently broken on CPU

Everything runs locally. Nothing leaves the machine.

Read it here: https://github.com/elbruno/ElBruno.S1Mini

#dotnet #AI #ONNX #SpeechToText #Whisper #LocalAI #CSharp

## X / Twitter variants

### Variant 1

`you don't have any any any any change at all?` -> `You don't have any change at all.`

I wrote about ElBruno.S1Mini: local ASR transcript cleanup for .NET using s1-mini + ONNX Runtime GenAI.

https://github.com/elbruno/ElBruno.S1Mini

### Variant 2

`you don't have any any any any change at all?` -> `You don't have any change at all.`

s1-mini is not chat and not speech-to-text. It cleans raw ASR transcripts. ElBruno.S1Mini brings that to .NET, locally.

NuGet: https://www.nuget.org/packages/ElBruno.S1Mini

### Variant 3 — thread opener

`you don't have any any any any change at all?` -> `You don't have any change at all.`

Short thread: what I learned building a local .NET transcript cleanup pipeline with Whisper, Silero VAD, s1-mini, and ONNX Runtime GenAI. 🧵
