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
    // shared sheet bitmap and every frame after that is a plain 1:1 blit. Sheets
    // grow on demand via a simple shelf packer.
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
        public void Draw(Graphics g, char ch, int style, Font font, Brush brush,
                         Brush halo, int x, int y, int wcells)
        {
            int key = ch | (style << 16);
            Slot s;
            if (!_slots.TryGetValue(key, out s)) s = Rasterize(key, ch, font, brush, halo, wcells);
            g.DrawImage(_sheets[s.Sheet], new Rectangle(x - Bleed, y - Bleed, s.W, s.H),
                        s.X, s.Y, s.W, s.H, GraphicsUnit.Pixel);
        }

        Slot Rasterize(int key, char ch, Font font, Brush brush, Brush halo, int wcells)
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
                        if (dx != 0 || dy != 0) DrawOne(ch, font, halo, gx + dx, gy + dy);
                DrawOne(ch, font, halo, gx - 2, gy);
                DrawOne(ch, font, halo, gx + 2, gy);
                DrawOne(ch, font, halo, gx, gy - 2);
                DrawOne(ch, font, halo, gx, gy + 2);
                DrawOne(ch, font, halo, gx - 3, gy);
                DrawOne(ch, font, halo, gx + 3, gy);
                DrawOne(ch, font, halo, gx, gy - 3);
                DrawOne(ch, font, halo, gx, gy + 3);
            }
            DrawOne(ch, font, brush, gx, gy);

            var s = new Slot { Sheet = _sheets.Count - 1, X = _x, Y = _y, W = w, H = h };
            _slots[key] = s;
            _x += w;
            return s;
        }

        void DrawOne(char ch, Font font, Brush brush, float gx, float gy)
        {
            _sg.DrawString(ch.ToString(), font, brush, gx, gy);
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

    // ---- The render engine: three depth layers, each in its script's native direction ----
    //   near (bright) : LTR headlines as horizontal tickers, heads sweeping left-to-right
    //   mid           : CJK headlines FALLING top-down as vertical columns (their
    //                   traditional reading direction); katakana gibberish if no CJK feed
    //   far (dim)     : RTL headlines (Arabic/Hebrew) as tickers whose heads sweep
    //                   right-to-left, characters upright in native order
    // A bright head flashes in the theme's head colour and ramps to the settled colour
    // over a few characters; a translucent black wash carries everything toward black.
    //
    // Layout is variable-width: a full-width CJK glyph occupies TWO grid cells, so
    // Japanese/Korean/Chinese line up with half-width Latin/Cyrillic on the same grid.
    class MatrixEngine : IDisposable
    {
        const int Ramp = 6;          // bright comet tail length behind each head
        const int Scramble = 2;      // how many of the newest placements shimmer (decode effect)
        const int Tiers = 3;         // 0 near LTR rows, 1 mid vertical CJK, 2 far RTL rows
        static readonly double[] TierDim = { 0.0, 0.42, 0.64 };

        // Glyph-cache style ids: tier*20 + {0 text, 1 fill, 2..2+Ramp rampText,
        // 10..10+Ramp rampFill}; then the clock specials below. A style is just
        // "which brush(es)", so cached pixels can be reused.
        const int StyleClock = 70;      // bright core of the converged time
        const int StyleClockDim = 71;   // softer anti-aliased edge of the digits

        readonly Random _rng = new Random();
        readonly Bitmap _buffer;
        readonly Graphics _g;

        // Depth is SIZE as much as brightness: each tier renders on its own grid
        // with progressively smaller type, so the back layers actually sit farther
        // away instead of just being dimmer copies of the front.
        readonly GlyphCache[] _cacheT = new GlyphCache[Tiers];
        readonly Font[] _fontT = new Font[Tiers];     // MS Gothic: Latin, Cyrillic, kana, Han
        readonly Font[] _fontKRT = new Font[Tiers];   // Malgun Gothic: Korean Hangul
        readonly Font[] _fontRTLT = new Font[Tiers];  // Tahoma: Hebrew / Arabic / Persian
        readonly int[] _cwT = new int[Tiers], _chT = new int[Tiers];
        readonly int[] _colsT = new int[Tiers], _rowsT = new int[Tiers];
        readonly int _cellW, _cellH, _cols, _rows;    // near-grid shorthand (clock, _scr)

        // Two independent horizontal row sets on different grids: set 0 = near LTR
        // tickers (tier 0), set 1 = far RTL tickers (tier 2). Not every row slot is
        // active — the gaps are where the layers behind show through.
        const int SetN = 0, SetF = 1;
        static int SetTier(int s) { return s == 0 ? 0 : 2; }
        readonly bool[][] _active = new bool[2][];
        readonly double[][] _pos = new double[2][];    // head progress, in characters
        readonly double[][] _speed = new double[2][];  // characters advanced per frame
        readonly int[][] _idx = new int[2][];          // characters committed so far
        readonly int[][] _cursor = new int[2][];       // next screen column (wraps)
        readonly int[][] _dirRow = new int[2][];       // +1 left-to-right, -1 right-to-left
        readonly string[][] _ribbon = new string[2][];
        readonly bool[][][] _filler = new bool[2][][];
        readonly byte[][][] _wide = new byte[2][][];   // cell width per char: 1 or 2

        // Tiny per-row history of the last few placements, so the head ramp can be
        // redrawn at the right columns each frame despite variable widths.
        readonly int[][][] _hCol = new int[2][][];
        readonly char[][][] _hCh = new char[2][][];
        readonly bool[][][] _hFl = new bool[2][][];
        readonly byte[][][] _hW = new byte[2][][];
        readonly int[][] _hPos = new int[2][];
        readonly int[][] _hCnt = new int[2][];

        volatile List<string> _pool;
        List<string> _poolLtr, _poolCjk, _poolRtl;   // the pool split by script
        readonly bool _decode;

        // What character currently occupies each NEAR-grid cell ('\x01' marks the
        // second cell of a wide glyph) — lets the clock re-illuminate the rain.
        readonly char[] _scr;

        // The mid-depth vertical layer: CJK text falling top-down on the mid grid.
        int _dropN;
        int[] _dCol; double[] _dPos, _dSpd; int[] _dIdx; string[] _dTxt;

        // National-debt risers: figures climbing bottom-to-top on the mid grid.
        int _riseN, _riseNext;
        int[] _uCol; double[] _uPos, _uSpd, _uHold; int[] _uIdx; string[] _uTxt;

        readonly SolidBrush _fade = new SolidBrush(Color.FromArgb(12, 0, 0, 0));
        readonly SolidBrush _glow;                                     // baked head bloom (near)
        readonly SolidBrush _glowMid;                                  // softer bloom (mid tier)
        readonly SolidBrush[] _baseText = new SolidBrush[Tiers];
        readonly SolidBrush[] _baseFill = new SolidBrush[Tiers];
        readonly SolidBrush[][] _rampText = new SolidBrush[Tiers][];   // [tier][depth]
        readonly SolidBrush[][] _rampFill = new SolidBrush[Tiers][];

        // The clock the rain converges into at the top of each minute.
        readonly bool _clockOn;
        public bool ForceClock;      // harness /dump verification only
        readonly SolidBrush _clockText, _clockEdge, _clockPlate;
        int _clockMinute = -1;
        byte[] _ckMask;              // per-cell ink coverage of the current HH:MM
        char[] _ckFill;              // fresh glyphs for lit cells the rain left empty
        int _ckGw, _ckGh, _ckC0, _ckR0;

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

            // Perspective: the mid layer at ~2/3 size, the far layer at ~1/2 size.
            int[] sizes = { fontSize, Math.Max(7, fontSize * 2 / 3), Math.Max(6, fontSize / 2) };
            for (int t = 0; t < Tiers; t++)
            {
                _fontT[t] = new Font("MS Gothic", sizes[t], FontStyle.Bold, GraphicsUnit.Pixel);
                _fontKRT[t] = new Font("Malgun Gothic", sizes[t], FontStyle.Bold, GraphicsUnit.Pixel);
                _fontRTLT[t] = new Font("Tahoma", sizes[t], FontStyle.Bold, GraphicsUnit.Pixel);
                SizeF m = _g.MeasureString("M", _fontT[t], PointF.Empty, StringFormat.GenericTypographic);
                _cwT[t] = Math.Max(1, (int)Math.Round(m.Width));   // half-width cell
                _chT[t] = Math.Max(1, (int)Math.Ceiling(_fontT[t].GetHeight(_g) * 1.18));
                _colsT[t] = Math.Max(2, w / _cwT[t]);
                _rowsT[t] = Math.Max(1, h / _chT[t]);
                _cacheT[t] = new GlyphCache(_cwT[t], _chT[t]);
            }
            _cellW = _cwT[0]; _cellH = _chT[0];
            _cols = _colsT[0]; _rows = _rowsT[0];

            Theme th = Theme.Get(Settings.ThemeKey);
            _decode = Settings.Decode;
            _glow = new SolidBrush(Color.FromArgb(52, th.Head));
            _glowMid = new SolidBrush(Color.FromArgb(24, th.Head));
            _clockOn = Settings.Clock;
            _clockText = new SolidBrush(Lerp(th.Head, th.Text, 0.40));
            _clockEdge = new SolidBrush(th.Text);
            _clockPlate = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
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

            _scr = new char[_rows * _cols];
            ClassifyPool();
            for (int s = 0; s < 2; s++)
            {
                int t = SetTier(s), rows = _rowsT[t];
                _active[s] = new bool[rows];
                _pos[s] = new double[rows];
                _speed[s] = new double[rows];
                _idx[s] = new int[rows];
                _cursor[s] = new int[rows];
                _dirRow[s] = new int[rows];
                _ribbon[s] = new string[rows];
                _filler[s] = new bool[rows][];
                _wide[s] = new byte[rows][];
                _hCol[s] = new int[rows][];
                _hCh[s] = new char[rows][];
                _hFl[s] = new bool[rows][];
                _hW[s] = new byte[rows][];
                _hPos[s] = new int[rows];
                _hCnt[s] = new int[rows];
                for (int r = 0; r < rows; r++)
                {
                    // Near rows stay sparse so the small layers behind show through;
                    // the far grid fills almost solid — a dense wall of tiny text.
                    _active[s][r] = _rng.NextDouble() < (s == SetN ? 0.55 : 0.90);
                    if (!_active[s][r]) continue;
                    BuildRibbon(s, r);
                    _speed[s][r] = s == SetN ? 0.45 + _rng.NextDouble() * 1.05
                                             : 0.10 + _rng.NextDouble() * 0.28;
                    _pos[s][r] = _rng.Next(0, Math.Max(1, _ribbon[s][r].Length));
                    _idx[s][r] = (int)Math.Floor(_pos[s][r]);
                    _cursor[s][r] = _rng.Next(0, _colsT[t]);
                    _hCol[s][r] = new int[Ramp];
                    _hCh[s][r] = new char[Ramp];
                    _hFl[s][r] = new bool[Ramp];
                    _hW[s][r] = new byte[Ramp];
                }
            }

            _dropN = Math.Max(2, _colsT[1] / 5);
            _dCol = new int[_dropN];
            _dPos = new double[_dropN];
            _dSpd = new double[_dropN];
            _dIdx = new int[_dropN];
            _dTxt = new string[_dropN];
            for (int i = 0; i < _dropN; i++)
            {
                SpawnDrop(i);
                // Stagger the first cycle so drops don't all start at the top edge.
                _dPos[i] = -_rng.NextDouble() * _rowsT[1] * 2;
                _dIdx[i] = (int)Math.Floor(_dPos[i]);
            }

            _riseN = Math.Min(Debts.Length, Math.Max(4, _colsT[1] / 24));
            _uCol = new int[_riseN];
            _uPos = new double[_riseN];
            _uSpd = new double[_riseN];
            _uHold = new double[_riseN];
            _uIdx = new int[_riseN];
            _uTxt = new string[_riseN];
            _riseNext = _rng.Next(Debts.Length);
            for (int i = 0; i < _riseN; i++) SpawnRiser(i);
        }

        public void SetPool(List<string> pool)
        {
            if (pool == null || pool.Count == 0) return;
            _pool = pool;
            ClassifyPool();
            for (int s = 0; s < 2; s++)
                for (int r = 0; r < _ribbon[s].Length; r++)
                    if (_active[s][r]) BuildRibbon(s, r);
        }

        // Split the pool by script so each depth layer can show text in its native
        // reading direction. Thresholds are loose because feed items mix scripts
        // ("Title :: English description" under a Japanese headline, etc).
        void ClassifyPool()
        {
            _poolLtr = new List<string>();
            _poolCjk = new List<string>();
            _poolRtl = new List<string>();
            foreach (string s in _pool)
            {
                int wide = 0, rtl = 0, n = 0;
                foreach (char c in s)
                {
                    if (c <= ' ') continue;
                    n++;
                    if (IsWide(c)) wide++;
                    else if (IsRtl(c)) rtl++;
                }
                if (n == 0) continue;
                if (rtl * 4 > n) _poolRtl.Add(BidiFix(s));
                else if (wide * 10 > n * 3) _poolCjk.Add(s);
                else _poolLtr.Add(s);
            }
        }

        // RTL tickers lay successive characters leftward — correct for Arabic and
        // Hebrew, but it mirrors any embedded Latin/digit run, so pre-reverse those
        // runs to cancel it out.
        static string BidiFix(string s)
        {
            char[] a = s.ToCharArray();
            int i = 0;
            while (i < a.Length)
            {
                if (!IsRtl(a[i]) && a[i] != ' ')
                {
                    int j = i;
                    while (j < a.Length && !IsRtl(a[j]) && a[j] != ' ') j++;
                    Array.Reverse(a, i, j - i);
                    i = j;
                }
                else i++;
            }
            return new string(a);
        }

        void BuildRibbon(int s, int r)
        {
            bool rtl = s == SetF && _poolRtl.Count > 0;
            _dirRow[s][r] = rtl ? -1 : 1;
            List<string> p = rtl ? _poolRtl
                           : _poolLtr.Count > 0 ? _poolLtr
                           : _poolCjk.Count > 0 ? _poolCjk
                           : new List<string>(Feed.Fallback);
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
                    for (int i = 0; i < gap; i++)
                    {
                        char g = rtl ? RtlGib() : Gib();
                        sb.Append(g); mask.Add(true); wid.Add(W(g));
                    }
                }
                else
                {
                    foreach (char c in "  •  ") { sb.Append(c); mask.Add(true); wid.Add(1); }
                }
                sb.Append(' '); mask.Add(false); wid.Add(1);
            }
            _ribbon[s][r] = sb.ToString();
            _filler[s][r] = mask.ToArray();
            _wide[s][r] = wid.ToArray();
        }

        char Gib()
        {
            int k = _rng.Next(0, 100);
            if (k < 78) return (char)(0xFF66 + _rng.Next(0, 0xFF9D - 0xFF66 + 1)); // half-width katakana
            if (k < 90) return (char)('0' + _rng.Next(0, 10));
            const string sym = "<>/*:=+#@$%";
            return sym[_rng.Next(sym.Length)];
        }

        // ---- National debt data ----
        // Gross general-government debt in LOCAL currency: ballpark IMF WEO /
        // national-treasury estimates pinned to DebtEpoch, extrapolated per second
        // from each nation's borrowing pace — so the figures are genuinely being
        // calculated as they climb. The currency sign leads, and the digits are
        // grouped the way that nation writes them.
        class DebtInfo
        {
            public readonly string Sign;
            public readonly double Base, PerYear;
            public readonly byte Fmt;
            public DebtInfo(string sign, double baseAmt, double perYear, byte fmt)
            { Sign = sign; Base = baseAmt; PerYear = perYear; Fmt = fmt; }
        }
        const byte FmtComma = 0, FmtDot = 1, FmtSpace = 2, FmtApos = 3, FmtIndian = 4, FmtArabic = 5;
        static readonly DateTime DebtEpoch = new DateTime(2026, 1, 1);
        static readonly DebtInfo[] Debts =
        {
            new DebtInfo("$",    38.3e12, 2.1e12,  FmtComma),   // United States
            new DebtInfo("¥",    1355e12, 12e12,   FmtComma),   // Japan (half-width yen)
            new DebtInfo("￥",   1.24e14, 1.3e13,  FmtComma),   // China (full-width yuan)
            new DebtInfo("€",    2.78e12, 9e10,    FmtDot),     // Germany
            new DebtInfo("€",    3.45e12, 1.3e11,  FmtSpace),   // France
            new DebtInfo("€",    3.05e12, 8e10,    FmtDot),     // Italy
            new DebtInfo("€",    1.66e12, 5e10,    FmtDot),     // Spain
            new DebtInfo("£",    2.95e12, 1.4e11,  FmtComma),   // United Kingdom
            new DebtInfo("₹",    1.98e14, 1.6e13,  FmtIndian),  // India (lakh/crore)
            new DebtInfo("R$",   9.6e12,  8e11,    FmtDot),     // Brazil
            new DebtInfo("$",    2.35e12, 7e10,    FmtComma),   // Canada
            new DebtInfo("$",    1.02e12, 5e10,    FmtComma),   // Australia
            new DebtInfo("₩",    1.26e15, 9e13,    FmtComma),   // South Korea
            new DebtInfo("₽",    3.2e13,  4e12,    FmtSpace),   // Russia
            new DebtInfo("$",    1.75e13, 1.4e12,  FmtComma),   // Mexico
            new DebtInfo("Rp",   8.9e15,  6e14,    FmtDot),     // Indonesia
            new DebtInfo("₺",    1.25e13, 3.5e12,  FmtDot),     // Turkey
            new DebtInfo("﷼",   1.35e12, 1.3e11,  FmtArabic),  // Saudi Arabia (Arabic-Indic digits)
            new DebtInfo("Fr.",  3.2e11,  5e9,     FmtApos),    // Switzerland
            new DebtInfo("€",    5.2e11,  1.5e10,  FmtDot),     // Netherlands
            new DebtInfo("zł",   2.05e12, 2.2e11,  FmtSpace),   // Poland
            new DebtInfo("kr",   2.25e12, 6e10,    FmtSpace),   // Sweden
            new DebtInfo("kr",   2.3e12,  7e10,    FmtSpace),   // Norway
            new DebtInfo("₪",    1.38e12, 9e10,    FmtComma),   // Israel
            new DebtInfo("$",    6.3e17,  9e16,    FmtDot),     // Argentina
            new DebtInfo("E£",   1.55e13, 2.5e12,  FmtComma),   // Egypt
            new DebtInfo("฿",    1.25e13, 7e11,    FmtComma),   // Thailand
            new DebtInfo("₫",    4.6e15,  4e14,    FmtDot),     // Vietnam
            new DebtInfo("€",    3.72e11, 4e9,     FmtDot),     // Greece
            new DebtInfo("R",    6.1e12,  5.5e11,  FmtSpace),   // South Africa
            new DebtInfo("₦",    1.5e14,  2.5e13,  FmtComma),   // Nigeria
            new DebtInfo("₱",    1.67e13, 1.4e12,  FmtComma),   // Philippines
            new DebtInfo("₨",    8.1e13,  1.1e13,  FmtIndian),  // Pakistan (lakh/crore)
            new DebtInfo("₴",    7.6e12,  1.4e12,  FmtSpace),   // Ukraine
        };

        static string FormatDebt(DebtInfo d)
        {
            double v = d.Base + d.PerYear * (DateTime.Now - DebtEpoch).TotalSeconds / 31557600.0;
            string digits = decimal.Truncate((decimal)Math.Max(0.0, v)).ToString();
            var sb = new StringBuilder(d.Sign);
            switch (d.Fmt)
            {
                case FmtIndian:
                    // Lakh/crore grouping: last three digits, then pairs (1,23,45,678).
                    int e = digits.Length;
                    var g = new List<string>();
                    g.Add(digits.Substring(Math.Max(0, e - 3)));
                    e -= 3;
                    while (e > 0)
                    {
                        int s2 = Math.Max(0, e - 2);
                        g.Insert(0, digits.Substring(s2, e - s2));
                        e = s2;
                    }
                    sb.Append(string.Join(",", g.ToArray()));
                    break;
                case FmtArabic:
                    foreach (char c in Group3(digits, '٬'))   // ٬ Arabic thousands mark
                        sb.Append(c >= '0' && c <= '9' ? (char)(0x0660 + (c - '0')) : c);
                    break;
                default:
                    sb.Append(Group3(digits, d.Fmt == FmtDot ? '.'
                                           : d.Fmt == FmtSpace ? ' '
                                           : d.Fmt == FmtApos ? '\'' : ','));
                    break;
            }
            return sb.ToString();
        }

        static string Group3(string digits, char sep)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(sep);
                sb.Append(digits[i]);
            }
            return sb.ToString();
        }

        // Gap filler for the RTL layer: Arabic letters instead of katakana, so the
        // far tickers stay visually coherent with their headlines.
        char RtlGib()
        {
            int k = _rng.Next(0, 100);
            if (k < 70) return (char)(0x0627 + _rng.Next(0, 0x064A - 0x0627 + 1));
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

        Font FontFor(int tier, char c)
        {
            if (IsRtl(c)) return _fontRTLT[tier];
            if ((c >= 0x20A0 && c <= 0x20CF) || c == 0x0E3F)   // currency signs (₹₽₺₪₴₦₱₨฿…)
                return _fontRTLT[tier];                        // Tahoma covers far more of them
            if ((c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x1100 && c <= 0x11FF) ||
                (c >= 0x3130 && c <= 0x318F)) return _fontKRT[tier];   // Hangul
            return _fontT[tier];
        }

        // Hebrew / Arabic / Persian — routes segments to the RTL layer, where heads
        // sweep right-to-left so successive characters appear in their native order.
        // (No shaping: Arabic letters render in isolated form, which suits the
        // one-glyph-per-cell Matrix grid anyway.)
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

        // Draw one glyph via its tier's atlas cache (each tier has its own type
        // size); the bloom halo, when given, is baked into the cached pixels, so
        // runtime cost is a single blit.
        void DrawGlyph(int tier, char ch, int style, Brush b, Brush halo, int x, int y, int wcells)
        {
            _cacheT[tier].Draw(_g, ch, style, FontFor(tier, ch), b, halo, x, y, wcells);
        }

        // Record what occupies a grid cell so the clock can re-illuminate the rain.
        void Mark(int row, int col, char ch, int w)
        {
            int b = row * _cols + col;
            _scr[b] = ch;
            if (w == 2 && col + 1 < _cols) _scr[b + 1] = '\x01';
        }

        static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        static int Mod(int a, int n) { int m = a % n; return m < 0 ? m + n : m; }

        void PushHist(int s, int r, int col, char ch, bool fl, byte w)
        {
            int p = _hPos[s][r];
            _hCol[s][r][p] = col; _hCh[s][r][p] = ch; _hFl[s][r][p] = fl; _hW[s][r][p] = w;
            _hPos[s][r] = Mod(p + 1, Ramp);
            if (_hCnt[s][r] < Ramp) _hCnt[s][r]++;
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
        // Layers draw far-to-near so same-frame overlaps resolve like real depth.
        public void Step(double dt)
        {
            int fa = (int)Math.Round(12 * dt);               // fade keeps pace with time
            _fade.Color = Color.FromArgb(Math.Max(3, Math.Min(60, fa)), 0, 0, 0);
            _g.FillRectangle(_fade, 0, 0, _buffer.Width, _buffer.Height);

            for (int r = 0; r < _rowsT[2]; r++) if (_active[SetF][r]) StepRow(SetF, r, dt);
            StepDrops(dt);
            StepRisers(dt);
            for (int r = 0; r < _rowsT[0]; r++) if (_active[SetN][r]) StepRow(SetN, r, dt);

            if (_clockOn)
            {
                DateTime now = DateTime.Now;
                double sec = now.Second + now.Millisecond / 1000.0;
                if (ForceClock || sec < 8.0) DrawClock(now, sec);
            }
        }

        void StepRow(int s, int r, double dt)
        {
            int tier = SetTier(s);
            int cw = _cwT[tier], chh = _chT[tier], cols = _colsT[tier];
            _pos[s][r] += _speed[s][r] * dt;
            int target = (int)Math.Floor(_pos[s][r]);
            string rib = _ribbon[s][r];
            bool[] mask = _filler[s][r];
            byte[] wid = _wide[s][r];
            int L = rib.Length;
            int y = r * chh;
            int dir = _dirRow[s][r];

            // Commit each newly reached character at its settled colour, advancing
            // the column cursor by the character's width — leftward on RTL rows —
            // and wrapping at the edge.
            while (_idx[s][r] < target)
            {
                int ci = Mod(_idx[s][r], L);
                char ch = rib[ci];
                if (ch == ' ')
                {
                    _cursor[s][r] += dir;
                    if (_cursor[s][r] >= cols) _cursor[s][r] = 0;
                    if (_cursor[s][r] < 0) _cursor[s][r] = cols - 1;
                }
                else
                {
                    int wcells = wid[ci];
                    int at;
                    if (dir > 0)
                    {
                        if (_cursor[s][r] + wcells > cols) _cursor[s][r] = 0;   // don't straddle the edge
                        at = _cursor[s][r];
                        _cursor[s][r] += wcells;
                        if (_cursor[s][r] >= cols) _cursor[s][r] = 0;
                    }
                    else
                    {
                        if (_cursor[s][r] - wcells + 1 < 0) _cursor[s][r] = cols - 1;
                        at = _cursor[s][r] - wcells + 1;
                        _cursor[s][r] -= wcells;
                        if (_cursor[s][r] < 0) _cursor[s][r] = cols - 1;
                    }
                    DrawGlyph(tier, ch, tier * 20 + (mask[ci] ? 1 : 0),
                              mask[ci] ? _baseFill[tier] : _baseText[tier], null,
                              at * cw, y, wcells);
                    if (s == SetN) Mark(r, at, ch, wcells);
                    PushHist(s, r, at, ch, mask[ci], (byte)wcells);
                }
                _idx[s][r]++;
            }

            // Redraw the last few placements as a bright head ramp. Each cell is
            // erased first because the decode shimmer can show a DIFFERENT glyph
            // than the one committed underneath.
            int cnt = _hCnt[s][r];
            for (int d = 0; d < cnt && d < Ramp; d++)
            {
                int p = Mod(_hPos[s][r] - 1 - d, Ramp);
                int wcells = _hW[s][r][p];
                int x = _hCol[s][r][p] * cw;
                _g.FillRectangle(Brushes.Black, x, y, wcells * cw, chh);

                char ch = (_decode && d < Scramble) ? ScrambleGlyph(wcells) : _hCh[s][r][p];
                int style = tier * 20 + (_hFl[s][r][p] ? 10 : 2) + d;
                Brush b = _hFl[s][r][p] ? _rampFill[tier][d] : _rampText[tier][d];

                // Near heads carry the full baked phosphor bloom; the far layer none.
                Brush halo = (d == 0 && tier == 0) ? _glow : null;
                DrawGlyph(tier, ch, style, b, halo, x, y, wcells);
            }
        }

        // The mid depth layer: Chinese/Japanese headlines falling top-down, one
        // glyph per row, in their native vertical reading direction. Without a CJK
        // feed the drops fall as full-width katakana gibberish (the film look).
        void SpawnDrop(int i)
        {
            _dCol[i] = _rng.Next(0, Math.Max(1, _colsT[1] - 1));
            _dSpd[i] = 0.12 + _rng.NextDouble() * 0.33;
            _dPos[i] = -(2.0 + _rng.NextDouble() * 30.0);    // delay before re-entering at the top
            _dIdx[i] = (int)Math.Floor(_dPos[i]);
            var cjk = _poolCjk;
            if (cjk != null && cjk.Count > 0)
                _dTxt[i] = cjk[_rng.Next(cjk.Count)];
            else
            {
                var sb = new StringBuilder();
                int n = 24 + _rng.Next(40);
                for (int k = 0; k < n; k++)
                    sb.Append((char)(0x30A1 + _rng.Next(0, 0x30FA - 0x30A1 + 1)));
                _dTxt[i] = sb.ToString();
            }
        }

        // The debt figures climb bottom-to-top: the currency sign leads at the
        // bottom and the amount reads upward, most-significant digit last. A fresh
        // value is computed at every respawn, so the numbers visibly grow.
        void SpawnRiser(int i)
        {
            int cols = _colsT[1];
            _uTxt[i] = FormatDebt(Debts[Mod(_riseNext++, Debts.Length)]);
            int lane = Math.Max(1, cols / _riseN);            // stratified so risers spread out
            _uCol[i] = Math.Min(cols - 2, i * lane + _rng.Next(Math.Max(1, lane - 2)));
            _uSpd[i] = 0.18 + _rng.NextDouble() * 0.30;
            _uPos[i] = -(2.0 + _rng.NextDouble() * 45.0);     // delay before entering
            _uIdx[i] = (int)Math.Floor(_uPos[i]);
            _uHold[i] = -1.0;
        }

        void StepRisers(double dt)
        {
            int cw = _cwT[1], chh = _chT[1], rows = _rowsT[1], cols = _colsT[1];
            for (int i = 0; i < _riseN; i++)
            {
                string txt = _uTxt[i];
                int L = txt.Length;
                if (_uHold[i] >= 0.0)
                {
                    // Fully written: hold the figure crisp for a few seconds, then
                    // respawn as the next nation (with a freshly calculated value).
                    _uHold[i] -= dt;
                    if (_uHold[i] < 0.0) { SpawnRiser(i); continue; }
                }
                else
                {
                    _uPos[i] += _uSpd[i] * dt;
                    int target = (int)Math.Floor(_uPos[i]);
                    if (target > _uIdx[i]) _uIdx[i] = Math.Min(target, L);
                    if (_uIdx[i] >= L || _uIdx[i] > rows) _uHold[i] = 100.0;
                }

                // Redraw the whole written portion every frame so the figure stays
                // readable against the fade until it respawns and melts away.
                int written = Math.Min(_uIdx[i], Math.Min(L, rows));
                bool done = _uHold[i] >= 0.0;
                for (int k = 0; k < written; k++)
                {
                    int row = rows - 1 - k;
                    char ch = txt[k];
                    if (ch == ' ') continue;                  // grouping gap (space-format nations)
                    int w = W(ch);
                    int col = Math.Min(_uCol[i], cols - w);
                    _g.FillRectangle(Brushes.Black, col * cw, row * chh, w * cw, chh);
                    int age = written - 1 - k;                // 0 = newest placement
                    bool fresh = !done && age < Ramp;
                    char show = (fresh && _decode && age < Scramble) ? ScrambleGlyph(w) : ch;
                    Brush b = fresh ? _rampText[1][age] : _rampText[1][2];
                    int style = 20 + 2 + (fresh ? age : 2);
                    Brush halo = fresh && age == 0 ? _glowMid : null;
                    DrawGlyph(1, show, style, b, halo, col * cw, row * chh, w);
                }
            }
        }

        void StepDrops(double dt)
        {
            int cw = _cwT[1], chh = _chT[1], rows = _rowsT[1], cols = _colsT[1];
            for (int i = 0; i < _dropN; i++)
            {
                _dPos[i] += _dSpd[i] * dt;
                int target = (int)Math.Floor(_dPos[i]);
                string txt = _dTxt[i];
                int L = txt.Length;
                bool respawn = false;

                // Commit newly reached characters at the settled mid-tier colour.
                // Character index == row, so the text reads top-to-bottom.
                while (_dIdx[i] < target)
                {
                    int row = _dIdx[i]++;
                    if (row < 0) continue;
                    if (row >= rows + Ramp) { respawn = true; break; }
                    if (row >= rows) continue;
                    char ch = txt[Mod(row, L)];
                    if (ch == ' ') continue;
                    int w = W(ch);
                    int col = Math.Min(_dCol[i], cols - w);
                    DrawGlyph(1, ch, 20, _baseText[1], null, col * cw, row * chh, w);
                }
                if (respawn) { SpawnDrop(i); continue; }

                // Bright comet head falling down the column, same treatment as the
                // ticker heads but with the softer mid-tier bloom.
                for (int d = 0; d < Ramp; d++)
                {
                    int row = _dIdx[i] - 1 - d;
                    if (row < 0 || row >= rows) continue;
                    char ch = txt[Mod(row, L)];
                    if (ch == ' ') continue;
                    int w = W(ch);
                    int col = Math.Min(_dCol[i], cols - w);
                    _g.FillRectangle(Brushes.Black, col * cw, row * chh, w * cw, chh);
                    char show = (_decode && d < Scramble) ? ScrambleGlyph(w) : ch;
                    DrawGlyph(1, show, 20 + 2 + d, _rampText[1][d], d == 0 ? _glowMid : null,
                              col * cw, row * chh, w);
                }
            }
        }

        // The time CONVERGES out of the rain. Once a minute HH:MM is rasterized from
        // a real font into a per-CELL ink-coverage mask (so the digits are font-smooth,
        // not blocky dot-matrix). While the clock is up, whatever characters the rain
        // has already left inside that mask re-illuminate brighter — live tickers and
        // falling columns keep writing through it, so the digits shimmer with real
        // content — and gaps fill with fresh glyphs. The fade then melts it back in.
        void DrawClock(DateTime now, double sec)
        {
            if (now.Minute != _clockMinute)
            {
                _clockMinute = now.Minute;
                BuildClockMask(now.ToString("HH:mm"));
            }
            if (_ckMask == null) return;

            // A gentle plate dims the rain around the digits so the bright pattern
            // reads clearly; it converges over a few frames and fades out with the rest.
            _g.FillRectangle(_clockPlate, (_ckC0 - 1) * _cellW, (_ckR0 - 1) * _cellH,
                             (_ckGw + 2) * _cellW, (_ckGh + 2) * _cellH);

            double reveal = sec < 1.5 ? sec / 1.5 : 1.0;
            for (int cr = 0; cr < _ckGh; cr++)
                for (int cc = 0; cc < _ckGw; cc++)
                {
                    int mi = cr * _ckGw + cc;
                    int m = _ckMask[mi];
                    if (m < 90) continue;                    // no ink at this cell

                    // Deterministic scatter: each cell converges at its own moment.
                    uint hsh = (uint)((cc * 73856093) ^ (cr * 19349663) ^ (_clockMinute * 83492791));
                    if (reveal < 1.0 && (hsh % 997) / 997.0 > reveal) continue;

                    // Re-illuminate whatever character the rain left here; brand-new
                    // glyphs only where the cell is empty.
                    int gcol = _ckC0 + cc, grow = _ckR0 + cr;
                    int at = gcol;
                    char ch = _scr[grow * _cols + gcol];
                    if (ch == '\x01' && gcol > 0) { at = gcol - 1; ch = _scr[grow * _cols + at]; }
                    if (ch <= ' ')
                    {
                        if (_ckFill[mi] == '\0' || _rng.Next(100) < 5) _ckFill[mi] = Gib();
                        ch = _ckFill[mi];
                        at = gcol;
                    }
                    int w = IsWide(ch) ? 2 : 1;
                    if (at + w > _cols) at = _cols - w;

                    bool core = m > 160;                     // solid ink vs anti-aliased edge
                    DrawGlyph(0, ch, core ? StyleClock : StyleClockDim,
                              core ? _clockText : _clockEdge, core ? _glow : null,
                              at * _cellW, grow * _cellH, w);
                }
        }

        // Rasterize HH:MM into a small bitmap and sample it per grid cell, producing
        // the ink-coverage mask the convergence draws from.
        void BuildClockMask(string t)
        {
            _ckMask = null;
            int gh = Math.Max(8, Math.Min((int)(_rows * 0.40), 22));
            float px = gh * _cellH * 0.95f;
            var sf = StringFormat.GenericTypographic;
            // A heavy face for the mask only (the pixels drawn are still rain glyphs):
            // thick strokes must span several grid cells or the digits crumble.
            Font f = ClockFont(px);
            try
            {
                SizeF sz = _g.MeasureString(t, f, PointF.Empty, sf);
                float maxW = _cols * _cellW * 0.80f;
                if (sz.Width > maxW)
                {
                    float k = maxW / sz.Width;
                    f.Dispose();
                    px *= k;
                    gh = Math.Max(8, (int)(gh * k));
                    f = ClockFont(px);
                    sz = _g.MeasureString(t, f, PointF.Empty, sf);
                }
                int gw = Math.Min(_cols - 2, (int)Math.Ceiling(sz.Width / _cellW) + 1);
                _ckGw = gw; _ckGh = gh;
                _ckC0 = (_cols - gw) / 2;
                _ckR0 = (_rows - gh) / 2;
                if (_ckC0 < 1 || _ckR0 < 1 || gw < 5) return;   // pane too small (preview)

                using (var bmp = new Bitmap(gw * _cellW, gh * _cellH, PixelFormat.Format32bppRgb))
                using (var bg = Graphics.FromImage(bmp))
                {
                    bg.Clear(Color.Black);
                    bg.TextRenderingHint = TextRenderingHint.AntiAlias;
                    bg.DrawString(t, f, Brushes.White,
                                  (bmp.Width - sz.Width) * 0.5f, (bmp.Height - sz.Height) * 0.5f, sf);

                    _ckMask = new byte[gw * gh];
                    _ckFill = new char[gw * gh];
                    for (int cr = 0; cr < gh; cr++)
                        for (int cc = 0; cc < gw; cc++)
                        {
                            int x0 = cc * _cellW, y0 = cr * _cellH;
                            int a = bmp.GetPixel(x0 + _cellW / 4, y0 + _cellH / 4).R
                                  + bmp.GetPixel(x0 + (3 * _cellW) / 4, y0 + _cellH / 4).R
                                  + bmp.GetPixel(x0 + _cellW / 4, y0 + (3 * _cellH) / 4).R
                                  + bmp.GetPixel(x0 + (3 * _cellW) / 4, y0 + (3 * _cellH) / 4).R;
                            _ckMask[cr * gw + cc] = (byte)(a / 4);
                        }
                }
            }
            finally { f.Dispose(); }
        }

        Font ClockFont(float px)
        {
            try { return new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel); }
            catch { return new Font(_fontT[0].FontFamily, px, FontStyle.Bold, GraphicsUnit.Pixel); }
        }

        public void Prewarm(int frames) { for (int i = 0; i < frames; i++) Step(); }

        public void Dispose()
        {
            for (int t = 0; t < Tiers; t++)
            {
                _cacheT[t].Dispose();
                _fontT[t].Dispose();
                _fontKRT[t].Dispose();
                _fontRTLT[t].Dispose();
            }
            _g.Dispose();
            _buffer.Dispose();
            _fade.Dispose();
            _glow.Dispose();
            _glowMid.Dispose();
            _clockText.Dispose();
            _clockEdge.Dispose();
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

            _clock.Text = "Clock: the rain converges into HH:MM each minute.";
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
