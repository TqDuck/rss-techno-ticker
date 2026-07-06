using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;

namespace MatrixSaver
{
    // ---- Entry point: parses the screensaver command-line contract ----
    //   /s            run full screen on every monitor
    //   /p <hwnd>     render inside the little preview pane in Settings
    //   /c[:<hwnd>]   show the configuration dialog
    //   /dump <path> [theme] [feedUrl|-] [clock]  (verification only) render to a PNG
    //   (no args)     run full screen (friendly default when double-clicked)
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Settings.Load();

            string mode = "s";
            if (args.Length > 0)
            {
                string a = args[0].Trim().ToLowerInvariant();
                if (a.StartsWith("/dump")) mode = "dump";
                else if (a.StartsWith("/c")) mode = "c";
                else if (a.StartsWith("/p")) mode = "p";
                else if (a.StartsWith("/s")) mode = "s";
            }

            if (mode == "dump")
            {
                string path = args.Length >= 2 ? args[1] : "matrix_dump.png";
                if (args.Length >= 3) Settings.ThemeKey = args[2];   // harness-only overrides
                if (args.Length >= 4 && args[3] != "-") Settings.Feeds = new List<string> { args[3] };
                try
                {
                    using (var eng = new MatrixEngine(960, 600, 16, Feed.FetchSegments()))
                    {
                        if (args.Length >= 5 && args[4] == "clock") eng.ForceClock = true;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < 260; i++) eng.Step();
                        sw.Stop();
                        eng.Buffer.Save(path, ImageFormat.Png);
                        File.WriteAllText(path + ".timing.txt", string.Format(
                            "260 frames in {0} ms ({1:0.00} ms/frame)",
                            sw.ElapsedMilliseconds, sw.ElapsedMilliseconds / 260.0));
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(path + ".error.txt", ex.ToString());
                }
                return;
            }

            if (mode == "c")
            {
                Application.Run(new ConfigForm());
                return;
            }

            if (mode == "p")
            {
                IntPtr preview = ParseHandle(args);
                if (preview == IntPtr.Zero) return;
                Application.Run(new MatrixForm(preview));
                return;
            }

            var forms = new List<Form>();
            foreach (Screen s in Screen.AllScreens)
                forms.Add(new MatrixForm(s.Bounds));
            Application.Run(new MultiScreenContext(forms));
        }

        static IntPtr ParseHandle(string[] args)
        {
            string digits = "";
            if (args.Length >= 2) digits = args[1];
            if (args[0].Length > 2)
            {
                string tail = args[0].Substring(2).TrimStart(':', ' ');
                if (tail.Length > 0) digits = tail;
            }
            long h;
            return long.TryParse(digits, out h) ? new IntPtr(h) : IntPtr.Zero;
        }
    }

    // ---- Persisted configuration (HKCU\Software\MatrixRain) ----
    static class Settings
    {
        const string Key = @"Software\MatrixRain";
        public const string DefaultFeed = "https://feeds.twit.tv/twit.xml";

        public static List<string> Feeds = new List<string>();
        public static int MaxLen = 0;       // 0 = no cap (full description)
        public static bool Filler = true;   // katakana gibberish in the gaps
        public static string ThemeKey = "matrix";
        public static bool Decode = true;   // characters shimmer before settling
        public static bool Clock = true;    // HH:MM materializes each minute

        public static void Load()
        {
            Feeds = new List<string>();
            MaxLen = 0;
            Filler = true;
            ThemeKey = "matrix";
            Decode = true;
            Clock = true;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    if (k != null)
                    {
                        var f = k.GetValue("Feeds") as string;
                        if (f != null)
                            foreach (var line in f.Split('\n'))
                            {
                                var t = line.Trim();
                                if (t.Length > 0) Feeds.Add(t);
                            }
                        object ml = k.GetValue("MaxLen"); if (ml is int) MaxLen = (int)ml;
                        object fl = k.GetValue("Filler"); if (fl is int) Filler = ((int)fl) != 0;
                        var th = k.GetValue("Theme") as string; if (th != null) ThemeKey = th;
                        object dc = k.GetValue("Decode"); if (dc is int) Decode = ((int)dc) != 0;
                        object ck = k.GetValue("Clock"); if (ck is int) Clock = ((int)ck) != 0;
                    }
                }
            }
            catch { }
            if (Feeds.Count == 0) Feeds.Add(DefaultFeed);
        }

        public static void Save()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                k.SetValue("Feeds", string.Join("\n", Feeds.ToArray()), RegistryValueKind.String);
                k.SetValue("MaxLen", MaxLen, RegistryValueKind.DWord);
                k.SetValue("Filler", Filler ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("Theme", ThemeKey, RegistryValueKind.String);
                k.SetValue("Decode", Decode ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("Clock", Clock ? 1 : 0, RegistryValueKind.DWord);
            }
        }
    }

    // ---- Color themes: head flash colour, settled headline colour, filler colour ----
    class Theme
    {
        public readonly string Name;
        public readonly Color Head, Text, Fill;
        Theme(string name, Color head, Color text, Color fill)
        {
            Name = name; Head = head; Text = text; Fill = fill;
        }

        public static readonly string[] Keys = { "matrix", "amber", "ice", "ghost", "crimson" };

        public static Theme Get(string key)
        {
            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "amber":
                    return new Theme("Amber CRT", Color.FromArgb(255, 244, 214),
                                     Color.FromArgb(255, 176, 0), Color.FromArgb(158, 92, 0));
                case "ice":
                    return new Theme("Ice Blue", Color.FromArgb(228, 246, 255),
                                     Color.FromArgb(90, 195, 255), Color.FromArgb(0, 100, 175));
                case "ghost":
                    return new Theme("Ghost White", Color.FromArgb(255, 255, 255),
                                     Color.FromArgb(204, 208, 216), Color.FromArgb(108, 112, 124));
                case "crimson":
                    return new Theme("Crimson", Color.FromArgb(255, 228, 224),
                                     Color.FromArgb(255, 64, 72), Color.FromArgb(152, 8, 28));
                default:
                    return new Theme("Matrix Green", Color.FromArgb(220, 255, 225),
                                     Color.FromArgb(45, 255, 95), Color.FromArgb(0, 145, 50));
            }
        }
    }

    // ---- Ticker text source: the configured RSS feeds, with an offline fallback ----
    static class Feed
    {
        public static readonly string[] Fallback =
        {
            "This Week in Tech 1090: Flock of SQLs :: AI-fueled memory shortages trigger sweeping price hikes on everything from Macs to game consoles.",
            "Hands-On Tech 273: ISP Email :: Why you should move off the email address your internet provider gave you, and how to do it cleanly.",
            "Untitled Linux Show 261: I Regret My Decisions :: Package managers, questionable kernel choices, and the joys of breaking your own system.",
            "This Week in Space 216: Dark Matter Intelligence :: What the latest surveys tell us about the invisible scaffolding of the universe.",
            "Security Now 1084: The Residential Proxy Threat :: How botnets quietly rent out home connections and what it means for defenders.",
            "Windows Weekly 989: Deer Hate MSDN :: Patch Tuesday, Copilot everywhere, and the latest from Redmond.",
            "iOS Today 808: Apple Design Awards 2026 :: The apps Apple thinks define great design this year, and how to use them.",
            "Intelligent Machines 876: It's No Melania :: Frontier models, agents, and where the AI hype meets reality.",
        };

        public static List<string> FetchSegments()
        {
            var list = new List<string>();
            foreach (string url in Settings.Feeds)
            {
                try { FetchOne(url, list); }
                catch { /* skip a feed that fails */ }
            }
            return list.Count > 0 ? list : new List<string>(Fallback);
        }

        public static void FetchOne(string url, List<string> list)
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            var req = (HttpWebRequest)WebRequest.Create(url);
            // Look like a browser — many feeds 403 a generic agent.
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/124.0 Safari/537.36";
            req.Accept = "application/rss+xml, application/atom+xml, application/xml, text/xml, */*";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;

            XDocument doc;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
                doc = XDocument.Load(stream);   // respects the feed's declared encoding

            // Accept both RSS (<item>) and Atom (<entry>), regardless of namespace.
            var entries = new List<XElement>();
            foreach (var e in doc.Descendants())
            {
                string ln = e.Name.LocalName;
                if (ln == "item" || ln == "entry") entries.Add(e);
            }

            int taken = 0;
            foreach (var item in entries)
            {
                if (taken >= 120) break;        // bound the pool per feed
                string title = Clean(TitleText(item), 160);
                string desc = Clean(FirstChildText(item, "description", "summary", "content", "subtitle"),
                                    Settings.MaxLen);
                if (title.Length == 0 && desc.Length == 0) continue;

                list.Add(desc.Length > 0 ? title + " :: " + desc : title);
                taken++;
            }
        }

        // Prefer the plain RSS <title>; fall back to any "title" (e.g. Atom's namespaced one),
        // which also dodges the duplicate <itunes:title>.
        static string TitleText(XElement item)
        {
            string any = "";
            foreach (var c in item.Elements())
            {
                if (c.Name.LocalName != "title") continue;
                if (c.Name.Namespace == XNamespace.None) return c.Value;
                if (any.Length == 0) any = c.Value;
            }
            return any;
        }

        static string FirstChildText(XElement item, params string[] locals)
        {
            foreach (string name in locals)
                foreach (var c in item.Elements())
                    if (c.Name.LocalName == name && c.Value.Length > 0) return c.Value;
            return "";
        }

        // Strip HTML, decode entities, drop control chars, collapse whitespace,
        // optionally truncate. Unicode letters are preserved so non-English feeds work.
        static string Clean(string s, int max)
        {
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, "<[^>]+>", " ");   // drop tags
            s = WebUtility.HtmlDecode(s);           // entities exposed after tag-strip

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(c < 0x20 || c == '�' ? ' ' : c);

            string r = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();

            if (r.Length > 0 && r.Length % 2 == 0)   // collapse an end-to-end doubled title
            {
                int half = r.Length / 2;
                string a = r.Substring(0, half).Trim();
                string b = r.Substring(half).Trim();
                if (a.Length > 0 && a == b) r = a;
            }

            if (max > 0 && r.Length > max)
            {
                int cut = r.LastIndexOf(' ', Math.Min(max, r.Length - 1));
                if (cut < max / 2) cut = max;
                r = r.Substring(0, cut).TrimEnd() + "...";
            }
            return r;
        }
    }

    // ---- Glyph atlas cache ----
    // GDI+ DrawString rasterizes the outline on every call and is the frame's hot
    // path. Instead, each distinct (character, style) pair is drawn ONCE into a
    // shared sheet bitmap — RTL rotation baked in — and every frame after that is
    // a plain 1:1 blit. Sheets grow on demand via a simple shelf packer.
    class GlyphCache : IDisposable
    {
        const int SheetW = 1024, SheetH = 512;
        const int Bleed = 4;    // margin captured around the cell so antialiasing
                                // and descender overhang survive the copy

        struct Slot { public int Sheet, X, Y, W, H; }

        readonly Dictionary<int, Slot> _slots = new Dictionary<int, Slot>();
        readonly List<Bitmap> _sheets = new List<Bitmap>();
        readonly int _cellW, _cellH;
        Graphics _sg;               // draws into the newest sheet
        int _x, _y, _rowH;

        public GlyphCache(int cellW, int cellH) { _cellW = cellW; _cellH = cellH; }

        // halo, when given, is baked INTO the cached pixels as a soft bloom around
        // the glyph — so a glowing head costs exactly one blit at runtime.
        public void Draw(Graphics g, char ch, int style, Font font, bool rtl, Brush brush,
                         Brush halo, int x, int y, int wcells)
        {
            int key = ch | (style << 16);
            Slot s;
            if (!_slots.TryGetValue(key, out s)) s = Rasterize(key, ch, font, rtl, brush, halo, wcells);
            g.DrawImage(_sheets[s.Sheet], new Rectangle(x - Bleed, y - Bleed, s.W, s.H),
                        s.X, s.Y, s.W, s.H, GraphicsUnit.Pixel);
        }

        Slot Rasterize(int key, char ch, Font font, bool rtl, Brush brush, Brush halo, int wcells)
        {
            int w = wcells * _cellW + 2 * Bleed, h = _cellH + 2 * Bleed;
            if (_sheets.Count == 0) NewSheet();
            if (_x + w > SheetW) { _x = 0; _y += _rowH; _rowH = 0; }
            if (_y + h > SheetH) NewSheet();
            if (h > _rowH) _rowH = h;

            float gx = _x + Bleed, gy = _y + Bleed;   // cell origin inside the slot
            if (halo != null)
            {
                // Three rings of low-alpha copies; overlaps compound near the glyph
                // and thin out with distance, approximating a phosphor bloom falloff.
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0) DrawOne(ch, font, rtl, halo, gx + dx, gy + dy);
                DrawOne(ch, font, rtl, halo, gx - 2, gy);
                DrawOne(ch, font, rtl, halo, gx + 2, gy);
                DrawOne(ch, font, rtl, halo, gx, gy - 2);
                DrawOne(ch, font, rtl, halo, gx, gy + 2);
                DrawOne(ch, font, rtl, halo, gx - 3, gy);
                DrawOne(ch, font, rtl, halo, gx + 3, gy);
                DrawOne(ch, font, rtl, halo, gx, gy - 3);
                DrawOne(ch, font, rtl, halo, gx, gy + 3);
            }
            DrawOne(ch, font, rtl, brush, gx, gy);

            var s = new Slot { Sheet = _sheets.Count - 1, X = _x, Y = _y, W = w, H = h };
            _slots[key] = s;
            _x += w;
            return s;
        }

        void DrawOne(char ch, Font font, bool rtl, Brush brush, float gx, float gy)
        {
            if (rtl)
            {
                // Rotate 180° about the cell centre (see IsRtl for why).
                float cx = gx + _cellW * 0.5f, cy = gy + _cellH * 0.5f;
                var st = _sg.Save();
                _sg.TranslateTransform(cx, cy);
                _sg.RotateTransform(180f);
                _sg.TranslateTransform(-cx, -cy);
                _sg.DrawString(ch.ToString(), font, brush, gx, gy);
                _sg.Restore(st);
            }
            else
            {
                _sg.DrawString(ch.ToString(), font, brush, gx, gy);
            }
        }

        const int MaxSheets = 24;   // ~24 MB; a huge CJK vocabulary can't grow unbounded

        void NewSheet()
        {
            if (_sg != null) _sg.Dispose();
            if (_sheets.Count >= MaxSheets)
            {
                // Start over; live glyphs re-rasterize on demand over the next frames.
                foreach (var b in _sheets) b.Dispose();
                _sheets.Clear();
                _slots.Clear();
            }
            // Premultiplied alpha is the GDI+ fast path — anything else forces a
            // per-pixel conversion on every single DrawImage.
            var bmp = new Bitmap(SheetW, SheetH, PixelFormat.Format32bppPArgb);
            _sheets.Add(bmp);
            _sg = Graphics.FromImage(bmp);
            _sg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _x = 0; _y = 0; _rowH = 0;
        }

        public void Dispose()
        {
            if (_sg != null) _sg.Dispose();
            foreach (var b in _sheets) b.Dispose();
            _sheets.Clear();
            _slots.Clear();
        }
    }

    // ---- The render engine: full-width horizontal tickers with Matrix illumination ----
    // Each row owns an endless ribbon (feed segments + gap filler). A bright head
    // sweeps left-to-right, wrapping forever. The head flashes in the theme's head
    // colour and ramps to the settled colour over a few characters; a translucent
    // black wash then carries everything toward black.
    //
    // Layout is variable-width: a full-width CJK glyph occupies TWO grid cells, so
    // Japanese/Korean/Chinese line up with half-width Latin/Cyrillic on the same grid.
    class MatrixEngine : IDisposable
    {
        const int Ramp = 6;          // bright comet tail length behind each head
        const int Scramble = 2;      // how many of the newest placements shimmer (decode effect)
        const int Tiers = 3;         // depth layers: 0 near (bright), 1 mid, 2 far (dim, slow)
        static readonly double[] TierDim = { 0.0, 0.30, 0.55 };

        // Glyph-cache style ids: tier*20 + {0 text, 1 fill, 2..2+Ramp rampText,
        // 10..10+Ramp rampFill}; then the specials below. A style is just "which
        // brush(es)", so cached pixels can be reused.
        const int StyleDust = 70;
        const int StyleClock = 71;

        readonly Random _rng = new Random();
        readonly Bitmap _buffer;
        readonly Graphics _g;
        readonly GlyphCache _cache;
        readonly Font _font;     // MS Gothic: Latin, Cyrillic, kana, most CJK Han
        readonly Font _fontKR;   // Malgun Gothic: Korean Hangul (MS Gothic lacks it)
        readonly Font _fontRTL;  // Tahoma: Hebrew / Arabic / Persian
        readonly int _cellW, _cellH, _cols, _rows;

        // Per-row stream state. The head advances in CHARACTERS; the screen column is
        // tracked separately because characters are 1 or 2 cells wide.
        readonly double[] _pos;      // head progress, in characters
        readonly double[] _speed;    // characters advanced per frame
        readonly int[] _idx;         // characters committed so far (absolute)
        readonly int[] _cursor;      // next screen column to place at (wraps)
        readonly string[] _ribbon;
        readonly bool[][] _filler;
        readonly byte[][] _wide;     // cell width per char: 1 or 2

        // Tiny per-row history of the last few placements, so the head ramp can be
        // redrawn at the right columns each frame despite variable widths.
        readonly int[][] _hCol;
        readonly char[][] _hCh;
        readonly bool[][] _hFl;
        readonly byte[][] _hW;
        readonly int[] _hPos;
        readonly int[] _hCnt;

        volatile List<string> _pool;

        // Depth: tier 0 rows are the foreground; deeper tiers are dimmer and slower,
        // as if the rain continues behind the front layer.
        readonly byte[] _tier;
        readonly bool _decode;

        readonly SolidBrush _fade = new SolidBrush(Color.FromArgb(12, 0, 0, 0));
        readonly SolidBrush _glow;                                     // baked head bloom (near)
        readonly SolidBrush _glowMid;                                  // softer bloom (mid tier)
        readonly SolidBrush _dust;                                     // ambient background twinkle
        readonly SolidBrush[] _baseText = new SolidBrush[Tiers];
        readonly SolidBrush[] _baseFill = new SolidBrush[Tiers];
        readonly SolidBrush[][] _rampText = new SolidBrush[Tiers][];   // [tier][depth]
        readonly SolidBrush[][] _rampFill = new SolidBrush[Tiers][];

        // The clock that materializes out of the rain at the top of each minute.
        readonly bool _clockOn;
        public bool ForceClock;      // harness /dump verification only
        readonly SolidBrush _clockText, _clockPlate;
        int _clockMinute = -1;
        char[] _clockCells;          // the shimmering glyphs the digits are made of

        public Bitmap Buffer { get { return _buffer; } }

        public MatrixEngine(int w, int h, int fontSize, List<string> pool)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            _pool = (pool != null && pool.Count > 0) ? pool : new List<string>(Feed.Fallback);

            _buffer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            _g = Graphics.FromImage(_buffer);
            _g.Clear(Color.Black);
            _g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // All glyphs arrive as 1:1 atlas blits — make DrawImage pixel-exact
            // and keep it on the premultiplied fast path.
            _g.InterpolationMode = InterpolationMode.NearestNeighbor;
            _g.PixelOffsetMode = PixelOffsetMode.Half;
            _g.CompositingQuality = CompositingQuality.HighSpeed;

            _font = new Font("MS Gothic", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            _fontKR = new Font("Malgun Gothic", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            _fontRTL = new Font("Tahoma", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF m = _g.MeasureString("M", _font, PointF.Empty, StringFormat.GenericTypographic);
            _cellW = Math.Max(1, (int)Math.Round(m.Width));   // half-width cell
            _cellH = Math.Max(1, (int)Math.Ceiling(_font.GetHeight(_g) * 1.18));
            _cols = Math.Max(2, w / _cellW);
            _rows = Math.Max(1, h / _cellH);
            _cache = new GlyphCache(_cellW, _cellH);

            Theme th = Theme.Get(Settings.ThemeKey);
            _decode = Settings.Decode;
            _glow = new SolidBrush(Color.FromArgb(52, th.Head));
            _glowMid = new SolidBrush(Color.FromArgb(24, th.Head));
            _dust = new SolidBrush(Color.FromArgb(80, Lerp(th.Fill, Color.Black, 0.35)));
            _clockOn = Settings.Clock;
            _clockText = new SolidBrush(Lerp(th.Head, th.Text, 0.45));
            _clockPlate = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            for (int tier = 0; tier < Tiers; tier++)
            {
                double dim = TierDim[tier];
                Color head = Lerp(th.Head, Color.Black, dim);
                Color text = Lerp(th.Text, Color.Black, dim);
                Color fill = Lerp(th.Fill, Color.Black, dim);
                _baseText[tier] = new SolidBrush(text);
                _baseFill[tier] = new SolidBrush(fill);
                _rampText[tier] = new SolidBrush[Ramp];
                _rampFill[tier] = new SolidBrush[Ramp];
                for (int d = 0; d < Ramp; d++)
                {
                    double t = (double)d / Ramp;     // 0 at head -> settled colour
                    _rampText[tier][d] = new SolidBrush(Lerp(head, text, t));
                    _rampFill[tier][d] = new SolidBrush(Lerp(head, fill, t));
                }
            }

            _pos = new double[_rows];
            _speed = new double[_rows];
            _idx = new int[_rows];
            _cursor = new int[_rows];
            _ribbon = new string[_rows];
            _filler = new bool[_rows][];
            _wide = new byte[_rows][];
            _hCol = new int[_rows][];
            _hCh = new char[_rows][];
            _hFl = new bool[_rows][];
            _hW = new byte[_rows][];
            _hPos = new int[_rows];
            _hCnt = new int[_rows];
            _tier = new byte[_rows];
            for (int r = 0; r < _rows; r++)
            {
                BuildRibbon(r);
                double dz = _rng.NextDouble();
                _tier[r] = dz < 0.40 ? (byte)0 : dz < 0.70 ? (byte)1 : (byte)2;
                _speed[r] = _tier[r] == 0 ? 0.45 + _rng.NextDouble() * 1.05
                          : _tier[r] == 1 ? 0.28 + _rng.NextDouble() * 0.62
                                          : 0.14 + _rng.NextDouble() * 0.34;
                _pos[r] = _rng.Next(0, Math.Max(1, _ribbon[r].Length));
                _idx[r] = (int)Math.Floor(_pos[r]);
                _cursor[r] = _rng.Next(0, _cols);
                _hCol[r] = new int[Ramp];
                _hCh[r] = new char[Ramp];
                _hFl[r] = new bool[Ramp];
                _hW[r] = new byte[Ramp];
            }
        }

        public void SetPool(List<string> pool)
        {
            if (pool == null || pool.Count == 0) return;
            _pool = pool;
            for (int r = 0; r < _rows; r++) BuildRibbon(r);
        }

        void BuildRibbon(int r)
        {
            var p = _pool;
            var sb = new StringBuilder();
            var mask = new List<bool>();
            var wid = new List<byte>();
            int targetLen = 1000 + _rng.Next(0, 700);

            while (sb.Length < targetLen)
            {
                string seg = p[_rng.Next(p.Count)];
                foreach (char c in seg) { sb.Append(c); mask.Add(false); wid.Add(W(c)); }

                if (Settings.Filler)
                {
                    int gap = 3 + _rng.Next(0, 16);
                    for (int i = 0; i < gap; i++) { char g = Gib(); sb.Append(g); mask.Add(true); wid.Add(W(g)); }
                }
                else
                {
                    foreach (char c in "  •  ") { sb.Append(c); mask.Add(true); wid.Add(1); }
                }
                sb.Append(' '); mask.Add(false); wid.Add(1);
            }
            _ribbon[r] = sb.ToString();
            _filler[r] = mask.ToArray();
            _wide[r] = wid.ToArray();
        }

        char Gib()
        {
            int k = _rng.Next(0, 100);
            if (k < 78) return (char)(0xFF66 + _rng.Next(0, 0xFF9D - 0xFF66 + 1)); // half-width katakana
            if (k < 90) return (char)('0' + _rng.Next(0, 10));
            const string sym = "<>/*:=+#@$%";
            return sym[_rng.Next(sym.Length)];
        }

        // Cell width: 2 for East-Asian full-width glyphs, else 1.
        static byte W(char c) { return IsWide(c) ? (byte)2 : (byte)1; }

        static bool IsWide(char c)
        {
            return (c >= 0x1100 && c <= 0x115F) ||  // Hangul Jamo
                   (c >= 0x2E80 && c <= 0x303E) ||  // CJK radicals / Kangxi / CJK symbols
                   (c >= 0x3041 && c <= 0x33FF) ||  // kana, CJK misc (full-width)
                   (c >= 0x3400 && c <= 0x4DBF) ||  // CJK Ext A
                   (c >= 0x4E00 && c <= 0x9FFF) ||  // CJK Unified
                   (c >= 0xA000 && c <= 0xA4CF) ||  // Yi
                   (c >= 0xAC00 && c <= 0xD7A3) ||  // Hangul syllables
                   (c >= 0xF900 && c <= 0xFAFF) ||  // CJK compat ideographs
                   (c >= 0xFE30 && c <= 0xFE4F) ||  // CJK compat forms
                   (c >= 0xFF00 && c <= 0xFF60) ||  // full-width forms
                   (c >= 0xFFE0 && c <= 0xFFE6);    // full-width signs
        }

        Font FontFor(char c)
        {
            if (IsRtl(c)) return _fontRTL;
            if ((c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x1100 && c <= 0x11FF) ||
                (c >= 0x3130 && c <= 0x318F)) return _fontKR;   // Hangul -> Malgun Gothic
            return _font;
        }

        // Hebrew / Arabic / Persian. We can't do real RTL+shaping, so instead we draw
        // these glyphs upside-down: flip your monitor 180° and a 180° per-glyph rotation
        // becomes upright AND the left-to-right layout reads right-to-left. (LTR text
        // then reads upside-down — that's the deal.)
        static bool IsRtl(char c)
        {
            return (c >= 0x0590 && c <= 0x05FF) ||  // Hebrew
                   (c >= 0x0600 && c <= 0x06FF) ||  // Arabic
                   (c >= 0x0750 && c <= 0x077F) ||  // Arabic Supplement
                   (c >= 0x08A0 && c <= 0x08FF) ||  // Arabic Extended-A
                   (c >= 0xFB1D && c <= 0xFB4F) ||  // Hebrew presentation forms
                   (c >= 0xFB50 && c <= 0xFDFF) ||  // Arabic presentation forms A
                   (c >= 0xFE70 && c <= 0xFEFF);    // Arabic presentation forms B
        }

        // Draw one glyph via the atlas cache; RTL rotation (and the bloom halo, when
        // given) are baked into the cached pixels, so runtime cost is a single blit.
        void DrawGlyph(char ch, int style, Brush b, Brush halo, int x, int y, int wcells)
        {
            _cache.Draw(_g, ch, style, FontFor(ch), IsRtl(ch), b, halo, x, y, wcells);
        }

        static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        static int Mod(int a, int n) { int m = a % n; return m < 0 ? m + n : m; }

        void PushHist(int r, int col, char ch, bool fl, byte w)
        {
            int p = _hPos[r];
            _hCol[r][p] = col; _hCh[r][p] = ch; _hFl[r][p] = fl; _hW[r][p] = w;
            _hPos[r] = Mod(p + 1, Ramp);
            if (_hCnt[r] < Ramp) _hCnt[r]++;
        }

        // Decode shimmer: a random stand-in glyph shown while a placement is still
        // "resolving". Width must match so the grid doesn't wobble.
        char ScrambleGlyph(int wcells)
        {
            if (wcells == 2) return (char)(0x30A1 + _rng.Next(0, 0x30FA - 0x30A1 + 1)); // full-width katakana
            return Gib();
        }

        public void Step() { Step(1.0); }

        // dt is in "frames" (1.0 = the nominal 33 ms tick). A late timer tick passes
        // dt > 1 so the rain covers the same distance it would have at full rate.
        public void Step(double dt)
        {
            int fa = (int)Math.Round(12 * dt);               // fade keeps pace with time
            _fade.Color = Color.FromArgb(Math.Max(3, Math.Min(60, fa)), 0, 0, 0);
            _g.FillRectangle(_fade, 0, 0, _buffer.Width, _buffer.Height);

            for (int r = 0; r < _rows; r++)
            {
                _pos[r] += _speed[r] * dt;
                int target = (int)Math.Floor(_pos[r]);
                string rib = _ribbon[r];
                bool[] mask = _filler[r];
                byte[] wid = _wide[r];
                int L = rib.Length;
                int y = r * _cellH;
                int tier = _tier[r];

                // Commit each newly reached character at its settled colour, advancing
                // the column cursor by the character's width and wrapping at the edge.
                while (_idx[r] < target)
                {
                    int ci = Mod(_idx[r], L);
                    char ch = rib[ci];
                    if (ch == ' ')
                    {
                        if (++_cursor[r] >= _cols) _cursor[r] = 0;
                    }
                    else
                    {
                        int wcells = wid[ci];
                        if (_cursor[r] + wcells > _cols) _cursor[r] = 0;   // don't straddle the edge
                        DrawGlyph(ch, tier * 20 + (mask[ci] ? 1 : 0),
                                  mask[ci] ? _baseFill[tier] : _baseText[tier], null,
                                  _cursor[r] * _cellW, y, wcells);
                        PushHist(r, _cursor[r], ch, mask[ci], (byte)wcells);
                        _cursor[r] += wcells;
                        if (_cursor[r] >= _cols) _cursor[r] = 0;
                    }
                    _idx[r]++;
                }

                // Redraw the last few placements as a bright head ramp. Each cell is
                // erased first because the decode shimmer can show a DIFFERENT glyph
                // than the one committed underneath.
                int cnt = _hCnt[r];
                for (int d = 0; d < cnt && d < Ramp; d++)
                {
                    int p = Mod(_hPos[r] - 1 - d, Ramp);
                    int wcells = _hW[r][p];
                    int x = _hCol[r][p] * _cellW;
                    _g.FillRectangle(Brushes.Black, x, y, wcells * _cellW, _cellH);

                    char ch = (_decode && d < Scramble) ? ScrambleGlyph(wcells) : _hCh[r][p];
                    int style = tier * 20 + (_hFl[r][p] ? 10 : 2) + d;
                    Brush b = _hFl[r][p] ? _rampFill[tier][d] : _rampText[tier][d];

                    // Heads carry a baked phosphor bloom (still one blit): strong on
                    // the near tier, soft on mid, none in the far dark.
                    Brush halo = d != 0 ? null : tier == 0 ? _glow : tier == 1 ? _glowMid : null;
                    DrawGlyph(ch, style, b, halo, x, y, wcells);
                }
            }

            // Ambient dust: faint translucent glyphs twinkling in the void between
            // tickers, so the dark behind the frontline reads as depth, not absence.
            int specks = (int)Math.Ceiling(_rows * 0.5 * dt);
            for (int i = 0; i < specks; i++)
                DrawGlyph(Gib(), StyleDust, _dust, null,
                          _rng.Next(_cols) * _cellW, _rng.Next(_rows) * _cellH, 1);

            if (_clockOn)
            {
                DateTime now = DateTime.Now;
                double sec = now.Second + now.Millisecond / 1000.0;
                if (ForceClock || sec < 8.0) DrawClock(now, sec);
            }
        }

        // 5x7 dot-matrix shapes for 0-9 (the colon is 2 wide). The clock is drawn as
        // RAIN — each lit dot is a shimmering katakana/digit glyph on the same grid
        // as the tickers, so the time looks like the code arranging itself.
        static readonly string[][] DigitShapes =
        {
            new[]{" ### ","#   #","#  ##","# # #","##  #","#   #"," ### "},
            new[]{"  #  "," ##  ","  #  ","  #  ","  #  ","  #  "," ### "},
            new[]{" ### ","#   #","    #","   # ","  #  "," #   ","#####"},
            new[]{" ### ","#   #","    #","  ## ","    #","#   #"," ### "},
            new[]{"   # ","  ## "," # # ","#  # ","#####","   # ","   # "},
            new[]{"#####","#    ","#### ","    #","    #","#   #"," ### "},
            new[]{" ### ","#    ","#    ","#### ","#   #","#   #"," ### "},
            new[]{"#####","    #","   # ","  #  "," #   "," #   "," #   "},
            new[]{" ### ","#   #","#   #"," ### ","#   #","#   #"," ### "},
            new[]{" ### ","#   #","#   #"," ####","    #","    #"," ### "},
        };
        static readonly string[] ColonShape = { "  ", "##", "##", "  ", "##", "##", "  " };

        // HH:MM materializes at screen centre at the top of each minute: lit dots
        // assemble in a random scatter, each dot a glowing glyph that keeps
        // shimmering while the clock is up; then the fade wash melts it back in.
        void DrawClock(DateTime now, double sec)
        {
            // Grid cells are much taller than wide, so a square-looking dot spans
            // several columns but one row. Scale the whole face to ~half the width
            // without letting it dominate vertically.
            int dotW = Math.Max(1, (int)Math.Round(_cellH / (double)_cellW));
            int scale = Math.Max(1, (int)Math.Round(_cols * 0.55 / (26.0 * dotW)));
            while (scale > 1 && 7 * scale > _rows / 2) scale--;
            int cw = dotW * scale;                           // cells per dot, horizontal
            int gw = 26 * cw, gh = 7 * scale;                // grid footprint in cells
            int c0 = (_cols - gw) / 2, r0 = (_rows - gh) / 2;
            if (c0 < 0 || r0 < 0) return;                    // pane too small (preview)

            if (now.Minute != _clockMinute || _clockCells == null || _clockCells.Length != gw * gh)
            {
                _clockMinute = now.Minute;
                if (_clockCells == null || _clockCells.Length != gw * gh)
                    _clockCells = new char[gw * gh];
                for (int i = 0; i < _clockCells.Length; i++) _clockCells[i] = Gib();
            }

            // The plate re-tints every frame, converging to near-black behind the
            // digits while the clock is up, then fading out with everything else.
            _g.FillRectangle(_clockPlate, (c0 - 2) * _cellW, (r0 - 1) * _cellH,
                             (gw + 4) * _cellW, (gh + 2) * _cellH);

            string t = now.ToString("HH:mm");
            double reveal = sec < 0.9 ? sec / 0.9 : 1.0;
            int shimmer = reveal < 1.0 ? 20 : 5;             // % of cells re-rolled per frame
            int cx = 0;
            for (int i = 0; i < t.Length; i++)
            {
                string[] shape = t[i] == ':' ? ColonShape : DigitShapes[t[i] - '0'];
                int dw = shape[0].Length;
                for (int dr = 0; dr < 7; dr++)
                    for (int dc = 0; dc < dw; dc++)
                    {
                        if (shape[dr][dc] != '#') continue;
                        // Deterministic scatter: each dot pops in at its own moment.
                        uint hsh = (uint)(((cx + dc) * 73856093) ^ (dr * 19349663) ^ (now.Minute * 83492791));
                        if (reveal < 1.0 && (hsh % 997) / 997.0 > reveal) continue;

                        // Erase the rain behind the dot so the digit stays crisp.
                        _g.FillRectangle(Brushes.Black, (c0 + (cx + dc) * cw) * _cellW,
                                         (r0 + dr * scale) * _cellH, cw * _cellW, scale * _cellH);
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < cw; sx++)
                            {
                                int col = (cx + dc) * cw + sx;
                                int row = dr * scale + sy;
                                int ci = row * gw + col;
                                if (_rng.Next(100) < shimmer) _clockCells[ci] = Gib();
                                DrawGlyph(_clockCells[ci], StyleClock, _clockText, _glow,
                                          (c0 + col) * _cellW, (r0 + row) * _cellH, 1);
                            }
                    }
                cx += dw + 1;
            }
        }

        public void Prewarm(int frames) { for (int i = 0; i < frames; i++) Step(); }

        public void Dispose()
        {
            _cache.Dispose();
            _g.Dispose();
            _buffer.Dispose();
            _font.Dispose();
            _fontKR.Dispose();
            _fontRTL.Dispose();
            _fade.Dispose();
            _glow.Dispose();
            _glowMid.Dispose();
            _dust.Dispose();
            _clockText.Dispose();
            _clockPlate.Dispose();
            for (int tier = 0; tier < Tiers; tier++)
            {
                _baseText[tier].Dispose();
                _baseFill[tier].Dispose();
                for (int d = 0; d < Ramp; d++) { _rampText[tier][d].Dispose(); _rampFill[tier][d].Dispose(); }
            }
        }
    }

    class MultiScreenContext : ApplicationContext
    {
        public MultiScreenContext(List<Form> forms)
        {
            foreach (Form f in forms)
            {
                f.FormClosed += (s, e) => ExitThread();
                f.Show();
            }
        }
    }

    class MatrixForm : Form
    {
        const int WS_CHILD = 0x40000000;
        const int WS_POPUP = unchecked((int)0x80000000);

        readonly bool _preview;
        readonly IntPtr _parent;
        MatrixEngine _engine;
        System.Windows.Forms.Timer _timer;
        System.Windows.Forms.Timer _refresh;
        readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        long _lastMs;

        public MatrixForm(Rectangle bounds)
        {
            _preview = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Cursor.Hide();
        }

        public MatrixForm(IntPtr parent)
        {
            _preview = true;
            _parent = parent;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;

            RECT r;
            if (GetClientRect(parent, out r))
                Size = new Size(r.Right - r.Left, r.Bottom - r.Top);
            Location = Point.Empty;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (_preview)
                {
                    cp.Style &= ~WS_POPUP;  // a borderless form is WS_POPUP; that beats WS_CHILD
                    cp.Style |= WS_CHILD;   // ...so strip it, then we truly embed in the pane
                    cp.Parent = _parent;
                }
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int fontSize = _preview ? 11 : 18;

            _engine = new MatrixEngine(ClientSize.Width, ClientSize.Height, fontSize,
                                       new List<string>(Feed.Fallback));
            _engine.Prewarm(_preview ? 60 : 120);

            if (!_preview)
            {
                FetchFeedsAsync();

                // Long sessions get fresh headlines: re-fetch every feed periodically
                // and swap the whole pool for that round's results.
                _refresh = new System.Windows.Forms.Timer();
                _refresh.Interval = 10 * 60 * 1000;
                _refresh.Tick += (s, ev) => FetchFeedsAsync();
                _refresh.Start();
            }

            _clock.Start();
            _lastMs = 0;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 33; // ~30 fps
            _timer.Tick += (s, ev) =>
            {
                // In preview mode, exit once the Settings pane (our parent) is gone —
                // otherwise the preview process leaks every time Settings refreshes.
                if (_preview && !IsWindow(_parent)) { Close(); return; }

                // WinForms timers drift and stall; scale each step by real elapsed
                // time so the rain's speed doesn't depend on timer health.
                long now = _clock.ElapsedMilliseconds;
                double dt = (now - _lastMs) / 33.3;
                _lastMs = now;
                if (dt <= 0) dt = 1;
                if (dt > 3) dt = 3;      // after a long stall, don't dump a flood of glyphs

                _engine.Step(dt);
                Invalidate();
            };
            _timer.Start();
        }

        // Fetch every feed on its own thread and merge each into the pool as it
        // arrives, so a slow / dead / blocked feed can't starve the others.
        void FetchFeedsAsync()
        {
            var bag = new List<string>();
            object gate = new object();
            foreach (string url in Settings.Feeds)
            {
                string u = url;
                var t = new Thread(() =>
                {
                    var part = new List<string>();
                    try { Feed.FetchOne(u, part); } catch { }
                    if (part.Count == 0) return;
                    List<string> snapshot;
                    lock (gate) { bag.AddRange(part); snapshot = new List<string>(bag); }
                    try { BeginInvoke((Action)(() => { if (_engine != null) _engine.SetPool(snapshot); })); }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_engine != null)
                e.Graphics.DrawImageUnscaled(_engine.Buffer, 0, 0);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        bool _haveMouse;
        Point _mouseStart;

        void Quit() { if (!_preview) Close(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_preview) return;
            if (!_haveMouse) { _haveMouse = true; _mouseStart = e.Location; return; }
            if (Math.Abs(e.X - _mouseStart.X) > 6 || Math.Abs(e.Y - _mouseStart.Y) > 6)
                Quit();
        }

        protected override void OnMouseDown(MouseEventArgs e) { Quit(); }
        protected override void OnKeyDown(KeyEventArgs e) { Quit(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_refresh != null) _refresh.Dispose();
                if (_engine != null) _engine.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
    }

    // ---- Configuration dialog (/c) ----
    class ConfigForm : Form
    {
        readonly ListBox _feeds = new ListBox();
        readonly NumericUpDown _maxLen = new NumericUpDown();
        readonly CheckBox _filler = new CheckBox();
        readonly ComboBox _theme = new ComboBox();
        readonly CheckBox _decode = new CheckBox();
        readonly CheckBox _clock = new CheckBox();

        public ConfigForm()
        {
            Text = "Matrix Rain Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 484);

            Controls.Add(new Label { Text = "RSS feeds (mix in other languages for an all-text Matrix):",
                                     Location = new Point(12, 10), AutoSize = true });

            _feeds.Location = new Point(12, 32);
            _feeds.Size = new Size(436, 160);
            _feeds.HorizontalScrollbar = true;
            foreach (var f in Settings.Feeds) _feeds.Items.Add(f);
            Controls.Add(_feeds);

            var add = new Button { Text = "Add...", Location = new Point(12, 198), Size = new Size(80, 26) };
            add.Click += (s, e) =>
            {
                string url = InputDialog.Show(this, "RSS feed URL:", "");
                if (!string.IsNullOrEmpty(url)) _feeds.Items.Add(url.Trim());
            };
            Controls.Add(add);

            var remove = new Button { Text = "Remove", Location = new Point(100, 198), Size = new Size(80, 26) };
            remove.Click += (s, e) =>
            {
                if (_feeds.SelectedIndex >= 0) _feeds.Items.RemoveAt(_feeds.SelectedIndex);
            };
            Controls.Add(remove);

            var test = new Button { Text = "Test all", Location = new Point(188, 198), Size = new Size(80, 26) };
            test.Click += (s, e) =>
            {
                var urls = new List<string>();
                foreach (var it in _feeds.Items) urls.Add(it.ToString());
                if (urls.Count == 0) { MessageBox.Show(this, "No feeds to test."); return; }

                test.Enabled = false;
                test.Text = "Testing...";
                var t = new Thread(() =>
                {
                    var sb = new StringBuilder();
                    foreach (string u in urls)
                    {
                        var part = new List<string>();
                        bool healthy; string status;
                        try
                        {
                            Feed.FetchOne(u, part);
                            healthy = part.Count > 0;
                            status = healthy ? part.Count + " items" : "fetched, but no items found";
                        }
                        catch (Exception ex) { healthy = false; status = ex.Message; }
                        sb.AppendLine((healthy ? "[ OK ]  " : "[FAIL]  ") + u);
                        sb.AppendLine("            " + status);
                    }
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            test.Enabled = true;
                            test.Text = "Test all";
                            MessageBox.Show(this, sb.ToString(), "Feed health");
                        }));
                    }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
            };
            Controls.Add(test);

            Controls.Add(new Label { Text = "Max description length (0 = full):",
                                     Location = new Point(12, 244), AutoSize = true });
            _maxLen.Location = new Point(250, 242);
            _maxLen.Size = new Size(90, 24);
            _maxLen.Minimum = 0;
            _maxLen.Maximum = 100000;
            _maxLen.Increment = 50;
            _maxLen.Value = Math.Max(0, Math.Min(100000, Settings.MaxLen));
            Controls.Add(_maxLen);

            _filler.Text = "Fill gaps with katakana (Matrix gibberish). Off = headlines only.";
            _filler.Location = new Point(12, 280);
            _filler.AutoSize = true;
            _filler.Checked = Settings.Filler;
            Controls.Add(_filler);

            Controls.Add(new Label { Text = "Color theme:",
                                     Location = new Point(12, 316), AutoSize = true });
            _theme.Location = new Point(250, 312);
            _theme.Size = new Size(198, 24);
            _theme.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var key in Theme.Keys) _theme.Items.Add(Theme.Get(key).Name);
            int sel = Array.IndexOf(Theme.Keys, (Settings.ThemeKey ?? "matrix").Trim().ToLowerInvariant());
            _theme.SelectedIndex = sel >= 0 ? sel : 0;
            Controls.Add(_theme);

            _decode.Text = "Decode effect (characters shimmer before settling).";
            _decode.Location = new Point(12, 348);
            _decode.AutoSize = true;
            _decode.Checked = Settings.Decode;
            Controls.Add(_decode);

            _clock.Text = "Clock: HH:MM materializes out of the rain each minute.";
            _clock.Location = new Point(12, 380);
            _clock.AutoSize = true;
            _clock.Checked = Settings.Clock;
            Controls.Add(_clock);

            var ok = new Button { Text = "OK", Location = new Point(268, 440), Size = new Size(80, 28),
                                  DialogResult = DialogResult.OK };
            ok.Click += (s, e) =>
            {
                Settings.Feeds = new List<string>();
                foreach (var it in _feeds.Items) Settings.Feeds.Add(it.ToString());
                Settings.MaxLen = (int)_maxLen.Value;
                Settings.Filler = _filler.Checked;
                Settings.ThemeKey = Theme.Keys[Math.Max(0, _theme.SelectedIndex)];
                Settings.Decode = _decode.Checked;
                Settings.Clock = _clock.Checked;
                try { Settings.Save(); } catch (Exception ex) { MessageBox.Show("Could not save: " + ex.Message); }
                Close();
            };
            Controls.Add(ok);

            var cancel = new Button { Text = "Cancel", Location = new Point(356, 440), Size = new Size(80, 28),
                                      DialogResult = DialogResult.Cancel };
            cancel.Click += (s, e) => Close();
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    // Minimal text-input dialog (WinForms has no built-in InputBox in C#).
    static class InputDialog
    {
        public static string Show(IWin32Window owner, string prompt, string def)
        {
            using (var f = new Form())
            {
                f.Text = "Matrix Rain";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.ClientSize = new Size(460, 120);

                f.Controls.Add(new Label { Text = prompt, Location = new Point(12, 12), AutoSize = true });
                var box = new TextBox { Location = new Point(12, 36), Size = new Size(436, 24), Text = def };
                f.Controls.Add(box);

                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK,
                                      Location = new Point(268, 76), Size = new Size(80, 28) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel,
                                          Location = new Point(356, 76), Size = new Size(80, 28) };
                f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;

                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
