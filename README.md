# rss-techno-ticker

**A native Windows screensaver that turns RSS feeds into a wall of Matrix-style tickers.**

Every screen row is an endless ribbon of real headlines — `TITLE :: DESCRIPTION` —
scrolling left-to-right and wrapping forever. Each character ignites **white**,
settles to **green**, and fades to **black** behind the writing head, just like
the digital rain. Optional katakana gibberish fills the gaps between stories for
the classic Matrix texture.

![English feed with katakana filler](preview.png)

Point it at feeds in any language and it becomes a live world tech ticker:

![Six languages, no filler](multilang.png)

Japanese, Korean, and Chinese render correctly on the same grid (full-width
glyphs take two cells):

![CJK feeds aligned with Latin](cjk.png)

## Features
- **Real Windows `.scr`** — handles `/s` (run), `/p` (settings-dialog preview),
  and `/c` (configure); multi-monitor; exits on mouse/keyboard.
- **No runtime to install** — a single ~25 KB executable built against the .NET
  Framework already present on every Windows machine.
- **Bring your own feeds** — RSS 2.0 *and* Atom. Feeds are fetched in parallel
  with a browser User-Agent; a slow or dead feed never blocks the others. Offline?
  A baked-in fallback keeps the rain falling.
- **Settings dialog** — add/remove feed URLs, cap or uncap description length,
  toggle the katakana filler. Stored under `HKCU\Software\MatrixRain`.
- **Wide language support** — see below. A curated list of verified feeds in a
  dozen languages lives in [FEEDS.md](FEEDS.md).

## Install
1. Grab `MatrixRain.scr` (or build it yourself — one command, see below).
2. Right-click it → **Install** (Windows 11: the option is right there in the
   context menu). The Screen Saver Settings dialog opens with it selected — set
   your wait time and click OK.
3. Or right-click → **Test** to run it full-screen immediately.

To make it a permanent entry in the screensaver dropdown, run `Install.ps1`
(right-click → Run with PowerShell; asks for admin to copy into the system folder).

By default it streams [TWiT.tv's This Week in Tech](https://twit.tv) public feed —
open **Configure** to replace it with anything you like.

## Language support
| Scripts | How they render |
|---|---|
| Latin & Cyrillic (English, German, French, Spanish, Portuguese, Turkish, Russian, Swahili, …) | Perfectly aligned |
| CJK (Japanese, Korean, Chinese) | Aligned — full-width glyphs span two cells; Korean via Malgun Gothic |
| RTL (Hebrew, Arabic, Persian) | Rendered **upside-down on purpose** 🙃 |

The RTL trick: flip your monitor 180° and the per-glyph 180° rotation turns the
text upright *while* the left-to-right layout becomes right-to-left — so it reads
correctly. (Your LTR feeds go upside-down instead. That's the trade.) Hebrew comes
out fully correct; Arabic/Persian letters render in isolated forms since there's
no shaping engine.

## Build from source
No IDE or SDK needed — the compiler ships with Windows:

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ `
    /out:MatrixRain.scr `
    /reference:System.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll `
    MatrixRain.cs
```

Or just run `build.ps1`.

## The screensaver contract
A `.scr` is an ordinary `.exe` that Windows invokes with a flag:

| Flag | What it does |
|------|--------------|
| `/s` | Run full screen on every monitor |
| `/p <hwnd>` | Render the preview thumbnail inside Screen Saver Settings |
| `/c` | Show the configuration dialog |
| `/dump <path>` | Render frames headlessly to a PNG (build verification / screenshots) |

Note: the `.scr` file association hijacks command-line arguments — to use `/dump`,
copy the file to a `.exe` name and invoke that directly.

## Tweaking the rendering (edit `MatrixRain.cs`, rebuild)
- **Colors** — `white` / `textGreen` / `fillGreen` in the `MatrixEngine` constructor.
- **Trail length** — the alpha of `_fade` (lower = longer-lived text).
- **Head glow length** — the `Ramp` constant.
- **Sweep speed** — the `_speed[r]` range in the constructor.
- **Text size** — `fontSize` in `MatrixForm.OnLoad` (18 full-screen, 11 preview).

## License
MIT — see [LICENSE](LICENSE).
