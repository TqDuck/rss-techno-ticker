# Multilingual tech RSS feeds (verified live)

All feeds below were fetched and confirmed working, RSS 2.0 (`<item>` format,
which the screensaver parses), with a live item count at time of checking.
Add them in **Configure → Add** and turn the **katakana filler off** for an
all-text multilingual ticker.

## Align cleanly today (Latin / Cyrillic script)
These render perfectly on the screensaver's grid.

| Language   | Blog              | Feed URL                                          | Items |
|------------|-------------------|---------------------------------------------------|-------|
| German     | Golem.de          | https://rss.golem.de/rss.php?feed=RSS2.0          | ~39   |
| German alt | t3n               | https://t3n.de/rss.xml                            | ~20   |
| Russian    | Habr              | https://habr.com/ru/rss/articles/?fl=ru           | ~40   |
| Russian alt| 3DNews            | https://3dnews.ru/news/rss/                       | ~60   |
| Spanish    | Xataka            | https://www.xataka.com/feedburner.xml             | ~26   |
| Spanish alt| Hipertextual      | https://hipertextual.com/feed                     | ~15   |

### Bonus languages (also align)
| Language   | Blog              | Feed URL                                          | Items |
|------------|-------------------|---------------------------------------------------|-------|
| Swahili 🌍 | BBC Swahili       | https://feeds.bbci.co.uk/swahili/rss.xml          | ~42   |
| French     | Korben            | https://korben.info/feed                          | ~20   |
| Portuguese | Tecnoblog         | https://tecnoblog.net/feed/                       | ~50   |
| Turkish 🕌 | Donanımhaber      | https://www.donanimhaber.com/rss/tum/             | ~50   |
| Turkish alt| ShiftDelete       | https://shiftdelete.net/feed                      | ~20   |
| Turkish alt| Webrazzi          | https://webrazzi.com/feed/                        | ~20   |

> Middle East note: **Turkish** is Latin-script and rides the foreground tickers
> (verified, including ı ğ ş ç ö ü). Arabic / Hebrew / Persian are right-to-left
> and get their own layer — see the RTL section below.

> Swahili note: dedicated Swahili-only *tech* blogs with RSS are scarce. BBC
> Swahili is general news but regularly covers Sayansi & Teknolojia, it's very
> active, and being Latin script it aligns perfectly.

## CJK — now render correctly ✅
Variable-width layout is in: full-width Japanese/Korean/Chinese glyphs take two
grid cells and align with the Latin text. Korean uses Malgun Gothic; everything
else uses MS Gothic (with system font-linking for Simplified Chinese).

| Language                       | Blog                  | Feed URL                                            | Items |
|--------------------------------|-----------------------|-----------------------------------------------------|-------|
| Japanese                       | Gigazine              | https://gigazine.net/news/rss_2.0/                  | ~30   |
| Japanese alt                   | ITmedia               | https://rss.itmedia.co.jp/rss/2.0/itmedia_all.xml   | ~50   |
| Korean                         | 전자신문 (etnews)      | https://rss.etnews.com/Section901.xml               | ~30   |
| Cantonese / HK (Traditional)   | Unwire.hk             | https://unwire.hk/feed/                             | ~10   |
| Mandarin (Simplified, mainland)| 少数派 (sspai)         | https://sspai.com/feed                              | ~10   |
| Mandarin (Traditional, Taiwan) | 科技新報 (TechNews)    | https://technews.tw/feed/                           | ~40   |

### Right-to-left — native direction on the far layer ✅
RTL feeds are auto-routed to the far depth layer, where ticker heads sweep
**right-to-left** so the text reads in its native order (embedded Latin/digit
runs are bidi-corrected). Hebrew is fully correct; Arabic/Persian letters render
in isolated forms (no shaping engine).

| Language | Blog            | Feed URL                               | Items |
|----------|-----------------|----------------------------------------|-------|
| Arabic   | BBC Arabic      | https://feeds.bbci.co.uk/arabic/rss.xml | ~30  |
| Hebrew   | Geektime        | https://www.geektime.co.il/feed/       | ~30   |
| Persian  | Zoomit          | https://www.zoomit.ir/feed/            | ~50   |

BBC Arabic is general news (tech-only Arabic feeds like aitnews block automated
requests); Geektime and Zoomit are proper tech blogs.

### Notes on the Chinese variants
Written Chinese is largely shared, so the real distinction is region/script, not
"Cantonese vs Mandarin" as written languages:
- **Unwire.hk** — Hong Kong, Cantonese-speaking, **Traditional** Chinese.
- **sspai** — mainland China, Mandarin, **Simplified** Chinese.
- **TechNews.tw** — Taiwan, Mandarin, **Traditional** Chinese.

### Notes
- **Atom feeds now work** (`<entry>` format) — e.g. GeekNews (news.hada.io),
  heise-atom. (GeekNews is still Korean, so it renders cramped until CJK support.)
- Bloter, ZDNet Korea, ddaily still block automated requests even with a browser
  User-Agent (Cloudflare) — a blocked feed is just skipped, it won't break the rest.
