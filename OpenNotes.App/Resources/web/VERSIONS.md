# Bundled web assets (offline rendering)

These files are committed so the app renders Mermaid and KaTeX fully offline.
They are served to WebView2 pages via `SetVirtualHostNameToFolderMapping`
(`https://opennotes.assets/…` — see `Services/WebViewAssets.cs`).

| Asset | Version | Source |
|---|---|---|
| `mermaid/mermaid.min.js` | **11.12.0** (UMD) | https://cdn.jsdelivr.net/npm/mermaid@11.12.0/dist/mermaid.min.js |
| `katex/katex.min.js` | **0.16.22** | https://cdn.jsdelivr.net/npm/katex@0.16.22/dist/katex.min.js |
| `katex/katex.min.css` | **0.16.22** | https://cdn.jsdelivr.net/npm/katex@0.16.22/dist/katex.min.css |
| `katex/fonts/*.woff2` | **0.16.22** (20 files) | https://cdn.jsdelivr.net/npm/katex@0.16.22/dist/fonts/ |

Rules when updating:
- **Mermaid must stay ≥ 11.3** (older versions break `radar-beta` and other newer diagram types).
- Bundle the **UMD** `mermaid.min.js`, never the ESM `.mjs` — the ESM build imports sibling chunk
  files by relative URL and cannot be shipped as a single file.
- KaTeX's CSS references `fonts/*.woff2` by relative URL, so the folder layout must be preserved.
  Only the `.woff2` variants are needed (the CSS tries woff2 first and WebView2 supports it).
