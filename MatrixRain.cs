using System;
using System.Collections.Generic;
using System.Drawing;
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
    //   /dump <path>  (verification only) render frames headlessly to a PNG
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
                try
                {
                    using (var eng = new MatrixEngine(960, 600, 16, Feed.FetchSegments()))
                    {
                        for (int i = 0; i < 260; i++) eng.Step();
                        eng.Buffer.Save(path, ImageFormat.Png);
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

        public static void Load()
        {
            Feeds = new List<string>();
            MaxLen = 0;
            Filler = true;
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

    // ---- The render engine: full-width horizontal tickers with Matrix illumination ----
    // Each row owns an endless ribbon (feed segments + gap filler). A bright head
    // sweeps left-to-right, wrapping forever. The head is white and ramps to green
    // over a few characters; a translucent black wash then carries green -> black.
    //
    // Layout is variable-width: a full-width CJK glyph occupies TWO grid cells, so
    // Japanese/Korean/Chinese line up with half-width Latin/Cyrillic on the same grid.
    class MatrixEngine : IDisposable
    {
        const int Ramp = 4;

        readonly Random _rng = new Random();
        readonly Bitmap _buffer;
        readonly Graphics _g;
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

        // Tiny per-row history of the last few placements, so the white->green ramp
        // can be redrawn at the right columns each frame despite variable widths.
        readonly int[][] _hCol;
        readonly char[][] _hCh;
        readonly bool[][] _hFl;
        readonly int[] _hPos;
        readonly int[] _hCnt;

        volatile List<string> _pool;

        readonly SolidBrush _fade = new SolidBrush(Color.FromArgb(22, 0, 0, 0));
        readonly SolidBrush _baseText, _baseFill;
        readonly SolidBrush[] _rampText = new SolidBrush[Ramp];
        readonly SolidBrush[] _rampFill = new SolidBrush[Ramp];

        public Bitmap Buffer { get { return _buffer; } }

        public MatrixEngine(int w, int h, int fontSize, List<string> pool)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            _pool = (pool != null && pool.Count > 0) ? pool : new List<string>(Feed.Fallback);

            _buffer = new Bitmap(w, h);
            _g = Graphics.FromImage(_buffer);
            _g.Clear(Color.Black);
            _g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            _font = new Font("MS Gothic", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            _fontKR = new Font("Malgun Gothic", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            _fontRTL = new Font("Tahoma", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF m = _g.MeasureString("M", _font, PointF.Empty, StringFormat.GenericTypographic);
            _cellW = Math.Max(1, (int)Math.Round(m.Width));   // half-width cell
            _cellH = Math.Max(1, (int)Math.Ceiling(_font.GetHeight(_g) * 1.18));
            _cols = Math.Max(2, w / _cellW);
            _rows = Math.Max(1, h / _cellH);

            Color white = Color.FromArgb(220, 255, 225);
            Color textGreen = Color.FromArgb(45, 255, 95);
            Color fillGreen = Color.FromArgb(0, 145, 50);
            _baseText = new SolidBrush(textGreen);
            _baseFill = new SolidBrush(fillGreen);
            for (int d = 0; d < Ramp; d++)
            {
                double t = (double)d / Ramp;     // 0 at head (white) -> base green
                _rampText[d] = new SolidBrush(Lerp(white, textGreen, t));
                _rampFill[d] = new SolidBrush(Lerp(white, fillGreen, t));
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
            _hPos = new int[_rows];
            _hCnt = new int[_rows];
            for (int r = 0; r < _rows; r++)
            {
                BuildRibbon(r);
                _speed[r] = 0.40 + _rng.NextDouble() * 1.05;
                _pos[r] = _rng.Next(0, Math.Max(1, _ribbon[r].Length));
                _idx[r] = (int)Math.Floor(_pos[r]);
                _cursor[r] = _rng.Next(0, _cols);
                _hCol[r] = new int[Ramp];
                _hCh[r] = new char[Ramp];
                _hFl[r] = new bool[Ramp];
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

        // Draw one glyph; RTL glyphs are rotated 180° about their cell centre.
        void DrawGlyph(char ch, Brush b, float x, float y)
        {
            Font f = FontFor(ch);
            if (IsRtl(ch))
            {
                float cx = x + _cellW * 0.5f, cy = y + _cellH * 0.5f;
                var st = _g.Save();
                _g.TranslateTransform(cx, cy);
                _g.RotateTransform(180f);
                _g.TranslateTransform(-cx, -cy);
                _g.DrawString(ch.ToString(), f, b, x, y);
                _g.Restore(st);
            }
            else
            {
                _g.DrawString(ch.ToString(), f, b, x, y);
            }
        }

        static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        static int Mod(int a, int n) { int m = a % n; return m < 0 ? m + n : m; }

        void PushHist(int r, int col, char ch, bool fl)
        {
            int p = _hPos[r];
            _hCol[r][p] = col; _hCh[r][p] = ch; _hFl[r][p] = fl;
            _hPos[r] = Mod(p + 1, Ramp);
            if (_hCnt[r] < Ramp) _hCnt[r]++;
        }

        public void Step()
        {
            _g.FillRectangle(_fade, 0, 0, _buffer.Width, _buffer.Height); // green -> black

            for (int r = 0; r < _rows; r++)
            {
                _pos[r] += _speed[r];
                int target = (int)Math.Floor(_pos[r]);
                string rib = _ribbon[r];
                bool[] mask = _filler[r];
                byte[] wid = _wide[r];
                int L = rib.Length;
                float y = r * _cellH;

                // Commit each newly reached character at its settled green, advancing
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
                        DrawGlyph(ch, mask[ci] ? _baseFill : _baseText, _cursor[r] * _cellW, y);
                        PushHist(r, _cursor[r], ch, mask[ci]);
                        _cursor[r] += wcells;
                        if (_cursor[r] >= _cols) _cursor[r] = 0;
                    }
                    _idx[r]++;
                }

                // Redraw the last few placements as a white -> green ramp (the head glow).
                int cnt = _hCnt[r];
                for (int d = 0; d < cnt && d < Ramp; d++)
                {
                    int p = Mod(_hPos[r] - 1 - d, Ramp);
                    char ch = _hCh[r][p];
                    DrawGlyph(ch, _hFl[r][p] ? _rampFill[d] : _rampText[d], _hCol[r][p] * _cellW, y);
                }
            }
        }

        public void Prewarm(int frames) { for (int i = 0; i < frames; i++) Step(); }

        public void Dispose()
        {
            _g.Dispose();
            _buffer.Dispose();
            _font.Dispose();
            _fontKR.Dispose();
            _fontRTL.Dispose();
            _fade.Dispose();
            _baseText.Dispose();
            _baseFill.Dispose();
            for (int d = 0; d < Ramp; d++) { _rampText[d].Dispose(); _rampFill[d].Dispose(); }
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
                // Fetch every feed on its own thread and merge each into the pool as it
                // arrives, so a slow / dead / blocked feed can't starve the others.
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

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 33; // ~30 fps
            _timer.Tick += (s, ev) =>
            {
                // In preview mode, exit once the Settings pane (our parent) is gone —
                // otherwise the preview process leaks every time Settings refreshes.
                if (_preview && !IsWindow(_parent)) { Close(); return; }
                _engine.Step();
                Invalidate();
            };
            _timer.Start();
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

        public ConfigForm()
        {
            Text = "Matrix Rain Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 380);

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

            var ok = new Button { Text = "OK", Location = new Point(268, 336), Size = new Size(80, 28),
                                  DialogResult = DialogResult.OK };
            ok.Click += (s, e) =>
            {
                Settings.Feeds = new List<string>();
                foreach (var it in _feeds.Items) Settings.Feeds.Add(it.ToString());
                Settings.MaxLen = (int)_maxLen.Value;
                Settings.Filler = _filler.Checked;
                try { Settings.Save(); } catch (Exception ex) { MessageBox.Show("Could not save: " + ex.Message); }
                Close();
            };
            Controls.Add(ok);

            var cancel = new Button { Text = "Cancel", Location = new Point(356, 336), Size = new Size(80, 28),
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
