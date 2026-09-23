# Product

## Register

product

## Users
People who translate text many times a day on Windows (emails, documents, chats, web pages) and want the DeepL workflow without sending text to a cloud service. They copy text in another app, press Alt+T, read the result, copy it back. Sessions are short and frequent; the window is a tool that appears, does one job and goes back to the tray.

## Product Purpose
DeepLocal is an offline translator for Windows that runs on local models served by Ollama. It exists so that private text never leaves the machine. Success: a translation in a couple of seconds with TranslateGemma 12B, zero configuration after install, and nothing to learn for anyone who has used DeepL.

## Brand Personality
Quiet, precise, trustworthy. The interface should feel like a familiar desktop utility, not a tech demo: the text is the protagonist, the chrome steps back. Copy is short and literal; labels say what happens.

## Anti-references
- The v1.0 look: dark neon cards, monospace text, emoji used as icons, default grey WinForms-style controls on a dark bar.
- Chat-style AI apps (bubbles, avatars, "thinking" animations): this is a translator, not an assistant.
- A pixel copy of DeepL: same layout and feel, but DeepLocal keeps its own name, logo and navy/blue identity.

## Design Principles
1. The text is the product. Large readable type, generous panes, nothing competing with the two texts.
2. Earned familiarity. Same layout as DeepL (language bar above two joined panes, swap in the middle), standard Windows affordances.
3. Honest state. Always show which model is running, whether Ollama is reachable, and what to do when something is missing (a button, not an error code).
4. Local first. Local models come first; cloud models are allowed but always labelled.
5. Out of the way. Tray, hotkey and window placement serve the "copy, translate, paste" loop.

## Accessibility & Inclusion
WCAG 2.2 AA contrast in both themes (text 4.5:1, large text and UI parts 3:1). Full keyboard use (Tab order, Ctrl+Enter, Esc, Alt+T). Right-to-left languages rendered right-to-left. Theme follows Windows light/dark. No motion beyond state feedback.
