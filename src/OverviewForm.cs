using System.Drawing.Drawing2D;

namespace IndepenDesk;

/// <summary>
/// Genel bakış (Mission Control benzeri): tüm monitörler ve masaüstleri kartlar halinde.
/// Kartlar o masaüstündeki bütün pencereleri listeler; pencereye tıklamak masaüstüne geçip
/// pencereyi öne getirir, sürüklemek başka masaüstüne taşır. Kart başlığı sürüklenip başka
/// bir kartın üzerine bırakılırsa masaüstü o konuma eklenir (aynı monitörde sıralama değişir,
/// başka monitörde masaüstü komple taşınır). Karttaki ✕ masaüstünü kapatır.
/// </summary>
internal sealed class OverviewForm : Form
{
    private static OverviewForm? _open;
    public static bool IsOpen => _open != null;

    private readonly DesktopManager _mgr;
    private readonly ToolTip _tips = new() { InitialDelay = 400 };

    /// <summary>Odak kaybından sonra kapatmayı geciktirir; odak geri gelirse iptal edilir.</summary>
    private readonly System.Windows.Forms.Timer _autoClose = new() { Interval = 250 };

    /// <summary>Açılış anı: ilk anlarda jest odağı düşürdüğü için odak kaybı yok sayılır.</summary>
    private readonly DateTime _openedAt = DateTime.UtcNow;
    private const int GraceMs = 900;

    // Renk paleti
    private static readonly Color BgColor = Color.FromArgb(23, 23, 27);
    private static readonly Color CardBg = Color.FromArgb(38, 38, 45);
    private static readonly Color CardBorder = Color.FromArgb(58, 58, 68);
    private static readonly Color Accent = Color.FromArgb(75, 141, 224);   // aktif masaüstü
    private static readonly Color DropAccent = Color.FromArgb(70, 170, 110); // bırakma hedefi
    private static readonly Color CloseHover = Color.FromArgb(226, 108, 108);
    private static readonly Color ChipBg = Color.FromArgb(53, 53, 61);
    private static readonly Color ChipHover = Color.FromArgb(72, 72, 84);
    private static readonly Color ChipBorder = Color.FromArgb(80, 80, 94);
    private static readonly Color TextDim = Color.FromArgb(150, 150, 162);

    // Kart geometrisi
    private const int CardW = 276;
    private const int CardH = 216;      // taban yükseklik (az pencereli kartlar)
    private const int ListTop = 40;
    private const int ListPad = 10;
    private const int ChipH = 28;
    private const int ChipGap = 3;
    private const int CloseW = 26;

    private sealed record WindowDrag(IntPtr Handle);
    private sealed record DesktopDrag(string Device, int LocalIndex);

    /// <summary>Açıksa kapatır, değilse hiçbir şey yapmaz (aşağı hareketinin karşılığı).</summary>
    public static void CloseIfOpen() => _open?.Close();

    private static DateTime _closedAt = DateTime.MinValue;
    private const int ReopenGuardMs = 400;

    public static void Toggle(DesktopManager mgr)
    {
        if (_open != null) { _open.Close(); return; }

        // Jest sırasında panel odağı bir an yitirip kapanmış olabilir; hemen yeniden açmak
        // kullanıcıya "kapanmıyor" gibi görünür. Kısa bir süre geçmeden yeniden açılmaz.
        if ((DateTime.UtcNow - _closedAt).TotalMilliseconds < ReopenGuardMs) return;

        var f = new OverviewForm(mgr);
        _open = f;
        f.FormClosed += (_, _) => { _open = null; _closedAt = DateTime.UtcNow; };
        f.Show();
        f.Activate();
    }

    private OverviewForm(DesktopManager mgr)
    {
        _mgr = mgr;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = BgColor;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;

        Native.GetCursorPos(out var pt);
        var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(pt.X, pt.Y)) ?? Screen.PrimaryScreen!;
        var b = screen.WorkingArea;
        Bounds = new Rectangle(b.Left + b.Width / 24, b.Top + b.Height / 24,
                               b.Width * 11 / 12, b.Height * 11 / 12);

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        // Odak kaybında hemen kapatmak jestlerle çakışıyor: dokunmatik yüzey hareketi panelin
        // odağını bir an düşürüyor ve panel, kısayol gelmeden kapanıyordu. Bu yüzden kapatma
        // kısa bir gecikmeyle yapılır ve odak geri gelirse iptal edilir.
        Deactivate += (_, _) => { if (!IsDisposed && !Disposing) _autoClose.Start(); };
        Activated += (_, _) => _autoClose.Stop();
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();

            // WM_TIMER kuyrukta kalmış olabilir: panel bu arada kapandıysa dokunma.
            if (IsDisposed || Disposing || !IsHandleCreated) return;

            if (Native.GetForegroundWindow() == Handle) return;

            // Açılıştan hemen sonraki odak kaybı jestin kendisinden olabilir: paneli kapatmak
            // yerine odağı geri almayı dene.
            if ((DateTime.UtcNow - _openedAt).TotalMilliseconds < GraceMs)
            {
                Activate();
                return;
            }

            Close();
        };
        FormClosed += (_, _) => { _autoClose.Stop(); _autoClose.Dispose(); };

        BuildUi();
    }

    private void BuildUi()
    {
        Controls.Clear();

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(22, 16, 22, 16)
        };
        Controls.Add(root);

        // Boş alana tıklamak paneli kapatır: panel etkinken Windows dokunmatik yüzey
        // jestlerinin kısayolunu iletmiyor, dolayısıyla kapatma jeste bağlı kalamaz.
        CloseOnClick(root);

        var titleLbl = new Label
        {
            Text = L.T("ov.title"),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 2)
        };
        CloseOnClick(titleLbl);
        root.Controls.Add(titleLbl);

        var legendLbl = new Label
        {
            Text = L.T("ov.legend"),
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 14)
        };
        CloseOnClick(legendLbl);
        root.Controls.Add(legendLbl);

        var layout = _mgr.GetLayout();

        // Kartlar bütün pencereleri gösterir: satırın yüksekliği en kalabalık masaüstüne göre
        // belirlenir. Bütçe, başlık şeritleri ve kenar boşlukları düşülerek hesaplanır; böylece
        // kart içi kaydırma ancak pencereler gerçekten ekrana sığmadığında devreye girer.
        int rows = Math.Max(layout.Count, 1);
        int budget = Math.Max(CardH, (ClientSize.Height - 70 * rows - 70) / rows);

        foreach (var mon in layout)
        {
            var monLbl = new Label
            {
                Text = "🖥  " + L.F("ov.monitor", mon.Ordinal),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(4, 10, 0, 4)
            };
            CloseOnClick(monLbl);
            root.Controls.Add(monLbl);

            int mostWindows = mon.Desktops.Max(d => d.Windows.Count);
            int wanted = ListTop + Math.Max(mostWindows, 1) * (ChipH + ChipGap) + ListPad;
            int cardH = Math.Clamp(wanted, CardH, budget);

            var row = new BufferedPanel
            {
                AutoSize = true,
                AllowDrop = true,
                Padding = new Padding(4),
                Margin = new Padding(0, 0, 0, 8)
            };
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AllowDrop = true,
                Location = new Point(4, 4)
            };
            row.Controls.Add(flow);

            // Satırın boş alanı: başka monitörden gelen masaüstünü bu monitörün sonuna alır.
            // (Kartların üzerine bırakma, kartın kendi hedefleriyle konumlu olarak işlenir.)
            bool rowHover = false;
            row.Paint += (_, e) =>
            {
                if (!rowHover) return;
                using var pen = new Pen(DropAccent, 2) { DashStyle = DashStyle.Dash };
                e.Graphics.DrawRectangle(pen, 1, 1, row.Width - 3, row.Height - 3);
            };
            void RowOver(object? s, DragEventArgs e)
            {
                if (e.Data?.GetData(typeof(DesktopDrag)) is DesktopDrag d && d.Device != mon.Device)
                {
                    e.Effect = DragDropEffects.Move;
                    if (!rowHover) { rowHover = true; row.Invalidate(); }
                }
            }
            void RowLeave(object? s, EventArgs e) { rowHover = false; row.Invalidate(); }
            void RowDrop(object? s, DragEventArgs e)
            {
                rowHover = false;
                if (e.Data?.GetData(typeof(DesktopDrag)) is DesktopDrag d && d.Device != mon.Device)
                {
                    _mgr.MoveDesktop(d.Device, d.LocalIndex, mon.Device, -1);
                    BuildUi();
                }
            }
            foreach (Control target in new Control[] { row, flow })
            {
                target.DragEnter += RowOver;
                target.DragOver += RowOver;
                target.DragLeave += RowLeave;
                target.DragDrop += RowDrop;
                CloseOnClick(target);   // kartların arasındaki boşluk da kapatır
            }

            root.Controls.Add(row);

            foreach (var desk in mon.Desktops)
                flow.Controls.Add(BuildDesktopCard(mon, desk, cardH));

            if (mon.Desktops.Count < DesktopManager.MaxDesktopsPerMonitor)
                flow.Controls.Add(BuildAddCard(mon.Device, cardH));
        }
    }

    // ---------- masaüstü kartı ----------

    private Control BuildDesktopCard(MonitorEntry mon, DesktopEntry desk, int cardH)
    {
        var card = new BufferedPanel
        {
            Size = new Size(CardW, cardH),
            BackColor = CardBg,
            Margin = new Padding(7),
            AllowDrop = true,
            Cursor = Cursors.Hand
        };

        bool dropHover = false;   // pencere bırakma vurgusu
        int insertSide = 0;       // masaüstü ekleme göstergesi: -1 sol, +1 sağ, 0 yok

        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool dropping = dropHover || insertSide != 0;
            Color border = dropping ? DropAccent : desk.IsCurrent ? Accent : CardBorder;
            using var pen = new Pen(border, dropping || desk.IsCurrent ? 2.5f : 1.5f);
            using var path = RoundedRect(new Rectangle(1, 1, card.Width - 3, card.Height - 3), 10);
            e.Graphics.DrawPath(pen, path);

            if (dropHover)
            {
                using var hint = new Font("Segoe UI", 10f, FontStyle.Bold);
                using var brush = new SolidBrush(DropAccent);
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                e.Graphics.DrawString(L.T("ov.drop"), hint, brush,
                    new Rectangle(0, card.Height - 28, card.Width, 24), sf);
            }

            if (insertSide != 0)
            {
                // Masaüstünün hangi tarafa ekleneceğini gösteren dikey çubuk
                using var bar = new Pen(DropAccent, 5);
                int x = insertSide < 0 ? 4 : card.Width - 5;
                e.Graphics.DrawLine(bar, x, 10, x, card.Height - 10);
            }
        };

        // ✕ — masaüstünü kapatır; pencereler komşu masaüstüne geçer
        bool canClose = mon.Desktops.Count > 1;
        if (canClose)
        {
            int targetLocal = desk.LocalIndex > 0 ? desk.LocalIndex - 1 : 1;
            int targetNumber = mon.Desktops[targetLocal].GlobalNumber;

            var close = new Label
            {
                Text = "✕",
                ForeColor = TextDim,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(CloseW, 22),
                Location = new Point(CardW - CloseW - 8, 9),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _tips.SetToolTip(close, L.F("ov.tip.close", targetNumber));
            close.MouseEnter += (_, _) => close.ForeColor = CloseHover;
            close.MouseLeave += (_, _) => close.ForeColor = TextDim;
            close.Click += (_, _) => { _mgr.CloseDesktop(mon.Device, desk.LocalIndex); BuildUi(); };
            card.Controls.Add(close);
            close.BringToFront();
        }

        // Başlık şeridi: masaüstünü taşımak/sıralamak için geniş sürükleme tutamacı
        var headerLbl = new Label
        {
            Text = L.F("ov.desktop", desk.GlobalNumber) +
                   (desk.Windows.Count > 0 ? "  ·  " + desk.Windows.Count : ""),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(10, 9),
            Size = new Size(CardW - 28 - (canClose ? CloseW : 0), 22),
            Cursor = Cursors.SizeAll,
            BackColor = Color.Transparent
        };
        _tips.SetToolTip(headerLbl, L.T("ov.tip.header"));
        AttachDragSource(headerLbl, () => new DesktopDrag(mon.Device, desk.LocalIndex));
        card.Controls.Add(headerLbl);

        if (desk.IsCurrent)
        {
            var badge = new Label
            {
                Text = L.T("ov.active"),
                ForeColor = Color.White,
                BackColor = Accent,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true,
                Padding = new Padding(5, 2, 5, 2),
                Cursor = Cursors.SizeAll
            };
            _tips.SetToolTip(badge, L.T("ov.tip.header"));
            AttachDragSource(badge, () => new DesktopDrag(mon.Device, desk.LocalIndex));
            card.Controls.Add(badge);
            badge.Location = new Point(CardW - badge.PreferredSize.Width - 12 - (canClose ? CloseW : 0), 10);
            badge.BringToFront();
        }

        // Pencere listesi: bütün pencereler listelenir, sığmazsa kart içinde kaydırılır
        int listH = cardH - ListTop - ListPad;
        var list = new Panel
        {
            Location = new Point(10, ListTop),
            Size = new Size(CardW - 20, listH),
            BackColor = CardBg,
            AutoScroll = true
        };
        card.Controls.Add(list);

        bool scrolls = desk.Windows.Count * (ChipH + ChipGap) > listH;
        int chipW = list.Width - (scrolls ? SystemInformation.VerticalScrollBarWidth + 2 : 0);

        int y = 0;
        foreach (var win in desk.Windows)
        {
            list.Controls.Add(BuildWindowChip(win, new Point(0, y), chipW));
            y += ChipH + ChipGap;
        }

        if (desk.Windows.Count == 0)
            list.Controls.Add(new Label
            {
                Text = L.T("ov.empty"),
                ForeColor = TextDim,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                Location = new Point(2, 4),
                AutoSize = true,
                BackColor = Color.Transparent
            });

        void SwitchHere() { _mgr.SwitchTo(mon.Device, desk.LocalIndex); Close(); }
        card.Click += (_, _) => SwitchHere();
        list.Click += (_, _) => SwitchHere();

        void CardOver(object? s, DragEventArgs e)
        {
            if (e.Data?.GetData(typeof(WindowDrag)) is WindowDrag)
            {
                e.Effect = DragDropEffects.Move;
                if (!dropHover) { dropHover = true; card.Invalidate(); }
                return;
            }
            if (e.Data?.GetData(typeof(DesktopDrag)) is DesktopDrag d)
            {
                if (d.Device == mon.Device && d.LocalIndex == desk.LocalIndex)
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }
                e.Effect = DragDropEffects.Move;
                int localX = card.PointToClient(new Point(e.X, e.Y)).X;
                int side = localX < card.Width / 2 ? -1 : 1;
                if (side != insertSide) { insertSide = side; card.Invalidate(); }
            }
        }

        card.DragEnter += CardOver;
        card.DragOver += CardOver;
        card.DragLeave += (_, _) => { dropHover = false; insertSide = 0; card.Invalidate(); };
        card.DragDrop += (_, e) =>
        {
            int side = insertSide;
            dropHover = false;
            insertSide = 0;

            if (e.Data?.GetData(typeof(WindowDrag)) is WindowDrag w)
            {
                _mgr.MoveWindowToDesktop(w.Handle, mon.Device, desk.LocalIndex);
                BuildUi();
                return;
            }
            if (e.Data?.GetData(typeof(DesktopDrag)) is DesktopDrag d &&
                !(d.Device == mon.Device && d.LocalIndex == desk.LocalIndex))
            {
                _mgr.MoveDesktop(d.Device, d.LocalIndex, mon.Device, desk.LocalIndex + (side > 0 ? 1 : 0));
                BuildUi();
            }
        };
        return card;
    }

    // ---------- pencere kutucuğu ----------

    private Control BuildWindowChip(WindowEntry win, Point location, int width)
    {
        var chip = new BufferedPanel
        {
            Location = location,
            Size = new Size(width, ChipH),
            BackColor = ChipBg,
            Cursor = Cursors.Hand
        };
        chip.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(ChipBorder, 1);
            using var path = RoundedRect(new Rectangle(0, 0, chip.Width - 1, chip.Height - 1), 7);
            e.Graphics.DrawPath(pen, path);
        };

        var grip = new Label
        {
            Text = "⠿",
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 11f),
            Location = new Point(7, 4),
            AutoSize = true,
            BackColor = Color.Transparent,
            Cursor = Cursors.SizeAll
        };
        chip.Controls.Add(grip);

        int textX = 26;
        var icon = Native.GetWindowSmallIcon(win.Handle);
        if (icon != null)
        {
            chip.Controls.Add(new PictureBox
            {
                Image = icon.ToBitmap(),
                Size = new Size(16, 16),
                Location = new Point(26, 6),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Transparent,
                Enabled = false
            });
            textX = 48;
        }

        var titleLbl = new Label
        {
            Text = win.Title,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(225, 225, 235),
            Font = new Font("Segoe UI", 9.2f),
            Location = new Point(textX, 5),
            Size = new Size(chip.Width - textX - 6, 18),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        chip.Controls.Add(titleLbl);

        // Kısaltılan başlığın tamamı ipucunda görünür
        string tip = win.Title + "\n" + L.T("ov.tip.window");
        _tips.SetToolTip(chip, tip);
        _tips.SetToolTip(titleLbl, tip);

        void Hover(bool on) => chip.BackColor = on ? ChipHover : ChipBg;
        void GoTo() { _mgr.ActivateWindow(win.Handle); Close(); }
        void OnUp(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                ShowWindowMenu(win, chip);
        }

        foreach (Control c in new Control[] { chip, grip, titleLbl })
        {
            c.MouseEnter += (_, _) => Hover(true);
            c.MouseLeave += (_, _) => Hover(false);
            c.MouseUp += OnUp;
            AttachDragSource(c, () => new WindowDrag(win.Handle), chip, GoTo);
        }
        return chip;
    }

    /// <summary>Sol tuşla sürüklemeyi yalnızca imleç sistem eşiğini aştıktan sonra başlatır;
    /// eşik aşılmadan bırakılırsa <paramref name="onClick"/> çalışır. Böylece aynı kutucuk
    /// hem tıklanabilir hem sürüklenebilir olur.</summary>
    private static void AttachDragSource(Control source, Func<object> payload,
                                         Control? initiator = null, Action? onClick = null)
    {
        Point origin = Point.Empty;
        bool armed = false;

        source.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            origin = e.Location;
            armed = true;
        };
        source.MouseUp += (_, e) =>
        {
            bool click = armed && e.Button == MouseButtons.Left;
            armed = false;
            if (click) onClick?.Invoke();
        };
        source.MouseMove += (_, e) =>
        {
            if (!armed || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - origin.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - origin.Y) < SystemInformation.DragSize.Height) return;
            armed = false;
            (initiator ?? source).DoDragDrop(new DataObject(payload()), DragDropEffects.Move);
        };
    }

    /// <summary>Pencereye sağ tık: her monitörün her masaüstüne taşıma menüsü
    /// (görev çubuğu menüsü genişletilemediği için karşılığı burasıdır).</summary>
    private void ShowWindowMenu(WindowEntry win, Control anchor)
    {
        var menu = new ContextMenuStrip();
        foreach (var mon in _mgr.GetLayout())
            foreach (var desk in mon.Desktops)
            {
                string label = L.F("ov.menu.move", mon.Ordinal, desk.GlobalNumber) +
                               (desk.IsCurrent ? L.T("ov.menu.activeSuffix") : "");
                var (device, local) = (mon.Device, desk.LocalIndex);
                menu.Items.Add(label, null, (_, _) =>
                {
                    _mgr.MoveWindowToDesktop(win.Handle, device, local);
                    BuildUi();
                });
            }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("ov.menu.goto"), null, (_, _) =>
        {
            Close();
            _mgr.ActivateWindow(win.Handle);
        });
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    // ---------- yeni masaüstü kartı ----------

    private Control BuildAddCard(string device, int cardH)
    {
        var card = new BufferedPanel
        {
            Size = new Size(72, cardH),
            BackColor = BgColor,
            Margin = new Padding(7),
            AllowDrop = true,
            Cursor = Cursors.Hand
        };
        bool hover = false;
        bool dropHover = false;
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = dropHover ? DropAccent : hover ? Accent : CardBorder;
            using var pen = new Pen(color, dropHover ? 2.5f : 1.5f) { DashStyle = DashStyle.Dash };
            using var path = RoundedRect(new Rectangle(1, 1, card.Width - 3, card.Height - 3), 10);
            e.Graphics.DrawPath(pen, path);
            using var font = new Font("Segoe UI", 20f);
            using var brush = new SolidBrush(color);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(dropHover ? "»" : "+", font, brush, card.ClientRectangle, sf);
        };
        _tips.SetToolTip(card, L.T("ov.tip.add"));
        card.MouseEnter += (_, _) => { hover = true; card.Invalidate(); };
        card.MouseLeave += (_, _) => { hover = false; card.Invalidate(); };
        card.Click += (_, _) => { _mgr.CreateDesktopAndSwitch(device); BuildUi(); };

        // Masaüstü bırakılırsa bu monitörün sonuna taşınır; pencere bırakılırsa
        // pencereyle birlikte yeni bir masaüstü açılır.
        void AddOver(object? s, DragEventArgs e)
        {
            if (e.Data?.GetData(typeof(DesktopDrag)) is not DesktopDrag &&
                e.Data?.GetData(typeof(WindowDrag)) is not WindowDrag) return;
            e.Effect = DragDropEffects.Move;
            if (!dropHover) { dropHover = true; card.Invalidate(); }
        }
        card.DragEnter += AddOver;
        card.DragOver += AddOver;
        card.DragLeave += (_, _) => { dropHover = false; card.Invalidate(); };
        card.DragDrop += (_, e) =>
        {
            dropHover = false;
            if (e.Data?.GetData(typeof(WindowDrag)) is WindowDrag w)
            {
                _mgr.CreateDesktopWithWindow(device, w.Handle);
                BuildUi();
                return;
            }
            if (e.Data?.GetData(typeof(DesktopDrag)) is DesktopDrag d)
            {
                _mgr.MoveDesktop(d.Device, d.LocalIndex, device, -1);
                BuildUi();
            }
        };
        return card;
    }

    /// <summary>Boş alana tıklandığında paneli kapatır. Panel etkinken Windows dokunmatik
    /// yüzey jestlerinin kısayolunu iletmediği için kapatmanın jestten bağımsız yolu budur.</summary>
    private void CloseOnClick(Control c) =>
        c.MouseClick += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) Close();
        };

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }
}
