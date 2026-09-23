![DeepLocal, light theme](docs/screenshot-light.png)

# DeepLocal, Offline Translator (WPF + Ollama)

### DeepLocal brings the DeepL workflow to Windows, fully offline. Your text never leaves your PC: translations run on local models served by Ollama, with TranslateGemma 12B as the default.

![DeepLocal, dark theme](docs/screenshot-dark.png)

# ✨ What's new in 2.0

- **New DeepL-style interface**: language bar above two joined panes, swap in the middle, light and dark theme that follows Windows (switched live).
- **TranslateGemma 12B by default**: Google's translation model, prompted with its official format.
- **All your Ollama models** in the model menu, read live from Ollama: installed models first, cloud models labelled, embedding models hidden.
- **One-click download**: if TranslateGemma 12B is missing, the menu shows a Download button with progress (cancel any time, it resumes later).
- **Translate as you type**, with text streaming in as the model writes it. Stop with Esc.
- **Real auto-detect**: "Detect language" stays on and shows what it found, e.g. "Italian (detected)". If the text is already in the target language, DeepLocal switches the target for you.
- **36 languages** (was 9), right-to-left for Arabic, Hebrew and Persian.
- **Interface in English or Italian**, chosen by the installer and changeable in Settings.
- **Clear errors with an action**: "Start Ollama", "Retry", "Open model menu" instead of HTTP codes.
- Bug fixes: Alt+T now always brings the window back from the tray, the tray menu stays in sync, no more 100 s timeout on large models, Japanese is no longer detected as Chinese, parallel translations cannot overwrite each other, the window fits small or scaled screens.

# 📦 Requirements

- Windows 10 (1809) or Windows 11, 64-bit
- [Ollama](https://ollama.com/download/windows) running at http://127.0.0.1:11434
- About 8 GB of disk for TranslateGemma 12B (downloaded from inside the app)

No .NET install needed: the installer includes the runtime.

# ⬇️ Download

Get the installers from the [Releases](https://github.com/ShinRalexis/DeepLocal/releases) page:

- `DeepLocal_Setup_EN.exe`, English installer and interface
- `DeepLocal_Setup_ITA.exe`, Italian installer and interface

Both upgrade an existing 1.0 installation in place. Verify them with `SHA256SUMS.txt`.
SmartScreen may warn about an unsigned app: click **More info**, then **Run anyway**.

# 🧠 Models

DeepLocal selects `translategemma:12b`. If it is not installed, open the model menu (top right) and click **Download**, or run:

```bash
ollama pull translategemma:12b
```

Any other model installed in Ollama appears in the same menu (gemma3, aya-expanse, mistral, qwen3, gpt-oss...). Cloud models (`*-cloud`) work too, after `ollama signin`, and are marked **Cloud**.

# ⌨️ Shortcuts

| Keys | Action |
|---|---|
| Alt+T | Translate the clipboard, from any app |
| Ctrl+Enter | Translate now |
| Esc | Stop the translation |
| Click on the tray icon | Show the window |
| Right click on the tray icon | Open, translate clipboard, hide, exit |

Closing or minimizing the window keeps DeepLocal in the tray, ready for Alt+T.

# 🧑‍💻 Build from source

```bash
git clone https://github.com/ShinRalexis/DeepLocal.git
cd DeepLocal
dotnet run
```

Requires the .NET 8 SDK. To build both installers (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Project layout:

```text
App.xaml(.cs)          tray, single instance, startup
MainWindow.xaml(.cs)   the translator window
Services/              Ollama client, prompts and language detection, settings, UI strings (IT/EN), theme
Themes/                Light.xaml, Dark.xaml (colours), Controls.xaml (styles)
installer/             Inno Setup script and build script
docs/                  PRODUCT.md, DESIGN.md, screenshots
```

# 🐞 Troubleshooting

- **"Ollama is not responding"**: start Ollama (the error panel has a button for it) and click Retry.
- **Model not found**: download it from the model menu, or `ollama pull <model>`.
- **Alt+T does nothing**: another app uses the same shortcut; the status bar says so at startup. Use the Translate clipboard button.
- **Window not visible**: click the tray icon.

# 🤝 Contributing

Fork, create a `feat/feature-name` branch, keep the XAML style consistent (colours in `Themes/`, texts in `Services/Loc.cs`), and open a PR with a description and screenshots.

# 📣 Support & Donations

If DeepLocal helps you, even $1 is a "hey friend, thanks!" ❤️

Liberapay: https://liberapay.com/MetaDarko/donate

Bitcoin: [1NjV2CfyLw42Ej9UmZEcroyqnmmKMJNCUx](https://www.blockchain.com/explorer/addresses/btc/1NjV2CfyLw42Ej9UmZEcroyqnmmKMJNCUx)

@MetaDarko: https://github.com/ShinRalexis

For bugs or ideas open an Issue with app version, Windows version, steps and screenshots.

# 📝 Licenses

Code (MIT)
Copyright © 2025-2026 MetaDarko.
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: the above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.

Images/Assets (CC BY 4.0)
The screenshots in /docs and the other graphic assets are © 2025-2026 MetaDarko and distributed under Creative Commons Attribution 4.0 International: you may use them freely, including commercially, provided that you attribute "MetaDarko DeepLocal" and indicate any changes.
Full text: https://creativecommons.org/licenses/by/4.0/

In short: the rights remain yours, but anyone can freely use the code and images (with attribution for assets).

TranslateGemma is a Google model distributed under the Gemma Terms of Use. DeepLocal is an independent project and is not affiliated with DeepL SE.
