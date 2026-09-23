# Design

## Visual theme
DeepL-style two-pane translator with DeepLocal identity. A navy header band (shared with the Windows 11 title bar colour) sits above a light or dark canvas; one rounded panel holds the language bar and the two text panes joined by a 1px divider. Theme follows Windows (light/dark), switched live.

## Colour
Strategy: Restrained. Tinted cool neutrals plus one blue accent, used only for the primary action, focus, selection and links. The navy comes from the v1.0 header, the blue from the "A" of the app icon.

| Token | Light | Dark | Use |
|---|---|---|---|
| HeaderBg | #0E1727 | #0E1727 | header band, title bar |
| WindowBg | #F2F4F7 | #0B1019 | canvas |
| Surface | #FFFFFF | #141B27 | source pane, popups |
| SurfaceAlt | #F7F8FA | #111822 | target pane |
| Border | #D9DEE6 | #273142 | panel and control borders |
| Ink | #141C28 | #E7EBF2 | body text |
| InkMuted | #566173 | #9CA7B8 | labels, counters (>= 4.5:1) |
| Accent | #1763C9 | #4A8FEA | primary action, focus |
| AccentSoft | #E7F0FB | #1A2A42 | selected row |
| Danger | #B42318 | #F97066 | errors |

## Typography
One family: Segoe UI Variable (fallback Segoe UI). Icons: Segoe Fluent Icons (fallback Segoe MDL2 Assets), never emoji.
Scale (ratio ~1.2): 12 caption, 13 label, 14 body UI, 16 section, 18 translation text, 20 brand.

## Components
- Language selector: flat text button with chevron; opens a multi-column list. Source has "Detect language", showing the detected language in brackets.
- Model picker: chip in the header (model name, chevron). Popup lists installed local models, then cloud models (Cloud badge), then models to download with a Download button and inline progress. TranslateGemma 12B is marked Recommended.
- Primary button: filled accent, 6px radius. Secondary/icon buttons: transparent, hover tint.
- Every interactive control has hover, pressed, focus (2px accent ring), disabled states.

## Layout
Header 56px. Canvas padding 20px. Panel radius 12px. Language bar 52px. Pane padding 20px. Status bar 28px.

## Motion
State only: 150ms hover fades, indeterminate progress line while translating or downloading.
