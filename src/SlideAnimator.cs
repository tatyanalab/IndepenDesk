using System.Diagnostics;

namespace IndepenDesk;

/// <summary>
/// macOS tarzı geçiş animasyonu. Geçişten hemen önce monitörün ekran görüntüsü alınıp
/// monitörü kaplayan sabit bir katmanda gösterilir; pencereler altta değiştirilir; ardından
/// görüntü katmanın İÇİNDE geçiş yönüne göre kayar ve boşalan bölge şeffaflaşarak yeni
/// masaüstünü ortaya çıkarır. Katman monitör sınırları dışına asla taşmaz.
/// </summary>
internal enum TransitionMode { Fade, Slide, None }

internal sealed class SlideAnimator : IDisposable
{
    private readonly Dictionary<string, SlideOverlay> _active = new();

    /// <summary>Geçiş efekti tercihi; tray menüsünden değiştirilir, settings.json'da saklanır.</summary>
    public static TransitionMode Mode { get; private set; } =
        SettingsStore.GetString("animation") switch
        {
            "slide" => TransitionMode.Slide,
            "fade" => TransitionMode.Fade,
            _ => TransitionMode.None   // varsayılan: efekt yok, geçiş anında olur
        };

    public static void SetMode(TransitionMode mode)
    {
        Mode = mode;
        SettingsStore.SetString("animation", mode switch
        {
            TransitionMode.Slide => "slide",
            TransitionMode.None => "none",
            _ => "fade"
        });
    }

    /// <summary>Geçiş başlamadan çağrılır: mevcut görüntüyü yakalar ve katmanı gösterir.</summary>
    public void Begin(string device, int direction)
    {
        Cancel(device);
        if (Mode == TransitionMode.None) return;

        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device);
        if (screen == null) return;

        try
        {
            var b = screen.Bounds;
            var shot = new Bitmap(b.Width, b.Height);
            using (var g = Graphics.FromImage(shot))
                g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);

            var overlay = new SlideOverlay(b, shot, direction, Mode);
            overlay.Completed += (_, _) =>
            {
                if (_active.TryGetValue(device, out var o) && o == overlay)
                    _active.Remove(device);
            };
            overlay.Show();
            _active[device] = overlay;
        }
        catch { /* ekran yakalama başarısızsa animasyonsuz geçilir */ }
    }

    /// <summary>Geçiş tamamlanınca çağrılır: kaydırmayı başlatır.</summary>
    public void Commit(string device)
    {
        if (_active.TryGetValue(device, out var overlay))
            overlay.SlideOut();
    }

    public void Cancel(string device)
    {
        if (_active.Remove(device, out var overlay))
            overlay.Dispose();
    }

    public void Dispose()
    {
        foreach (var o in _active.Values) o.Dispose();
        _active.Clear();
    }

    private sealed class SlideOverlay : Form
    {
        private const int SlideMs = 230;
        private const int FadeMs = 170;
        private const int PaintDelayMs = 40; // yeni pencerelerin altta çizilmesi için kısa bekleme

        private readonly Bitmap _shot;
        private readonly int _direction; // +1: ileri geçiş → görüntü sola kayar, -1: tersi
        private readonly TransitionMode _mode;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
        private readonly Stopwatch _clock = new();
        private int _paintDelay = PaintDelayMs;
        private int _imageX;
        private bool _sliding;

        public event EventHandler? Completed;

        public SlideOverlay(Rectangle bounds, Bitmap shot, int direction, TransitionMode mode)
        {
            _shot = shot;
            _direction = direction;
            _mode = mode;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;

            _timer.Tick += OnTick;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */
                            | 0x00000080 /* WS_EX_TOOLWINDOW */
                            | 0x00000020 /* WS_EX_TRANSPARENT: tıklamalar alta geçsin */;
                return cp;
            }
        }

        public void SlideOut()
        {
            if (_sliding) return;
            _sliding = true;
            _timer.Start(); // önce kısa bekler, sonra kaydırır
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (!_clock.IsRunning)
            {
                _paintDelay -= _timer.Interval;
                if (_paintDelay <= 0) _clock.Start();
                return;
            }

            int duration = _mode == TransitionMode.Slide ? SlideMs : FadeMs;
            double t = Math.Min(1.0, _clock.ElapsedMilliseconds / (double)duration);

            if (_mode == TransitionMode.Slide)
            {
                double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
                int offset = (int)(eased * Width);

                // Görüntü katmanın içinde kayar; boşalan şerit bölge dışına alınır (şeffaflaşır),
                // böylece alttaki gerçek yeni masaüstü görünür. Katman kendisi hiç hareket etmez.
                var shown = _direction > 0
                    ? new Rectangle(0, 0, Math.Max(0, Width - offset), Height)
                    : new Rectangle(offset, 0, Math.Max(0, Width - offset), Height);
                _imageX = _direction > 0 ? -offset : offset;

                var previous = Region;      // Region ataması eskisini serbest bırakmaz
                Region = new Region(shown);
                previous?.Dispose();
            }
            else
            {
                // Eski masaüstünün görüntüsü yumuşakça saydamlaşır; yön oyunu olmadığı için
                // tek yönlü kaymanın yarattığı "perde" hissi oluşmaz.
                Opacity = 1.0 - t * t * (3 - 2 * t); // smoothstep
            }
            Invalidate();

            if (t >= 1.0)
            {
                _timer.Stop();
                Completed?.Invoke(this, EventArgs.Empty);
                Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(_shot, _imageX, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _shot.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
