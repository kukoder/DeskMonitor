using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskMonitor
{
    public class MainForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int WM_MOVING = 0x0216;

        private const int HeaderHeight = 24;
        private const int ResizeBorderWidth = 5;

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_EXITSIZEMOVE = 0x0232;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private readonly AppSettings _settings;
        private readonly SystemMetrics _metrics;
        private readonly List<MetricSnapshot> _history;
        private readonly List<PingTargetTracker> _pingTrackers;
        private readonly NetworkTrafficTracker _trafficTracker;
        private readonly System.Windows.Forms.Timer _updateTimer;

        private MetricSnapshot _latestSnapshot;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _contextMenu;
        private ContextMenuStrip _trayContextMenu;

        // Header buttons (Close, Minimize, Settings)
        private Rectangle _rectBtnClose;
        private Rectangle _rectBtnMin;
        private Rectangle _rectBtnSettings;

        private bool _isCloseHovered;
        private bool _isMinHovered;
        private bool _isSettingsHovered;

        // Mouse dragging state
        private bool _isDragging = false;
        private Point _dragStartPoint;

        // Bottom IP Section
        private string _localIp = "...";
        private string _externalIp = "...";
        private bool _isRefreshingIp = false;
        private Rectangle _rectBtnRefreshIp;
        private bool _isRefreshIpHovered;

        // Top CPU Processes
        private readonly ProcessCpuTracker _processTracker;
        private TopProcessesForm _topProcForm;
        private Rectangle _rectBtnLightning = Rectangle.Empty;
        private bool _isLightningHovered = false;

        public MainForm(AppSettings settings)
        {
            _settings = settings;
            _metrics = new SystemMetrics();
            _history = new List<MetricSnapshot>();
            _pingTrackers = new List<PingTargetTracker>();
            _trafficTracker = new NetworkTrafficTracker();
            _processTracker = new ProcessCpuTracker(_settings);
            _processTracker.Start();

            this.FormBorderStyle = FormBorderStyle.None;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            // Minimum size allows shrinking down to 140px width!
            this.MinimumSize = new Size(140, 200);
            this.BackColor = Color.FromArgb(24, 24, 37);

            try
            {
                this.Icon = CreateTrayIcon();
            }
            catch { }

            InitializeTray();
            InitializeContextMenu();

            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Tick += OnTimerTick;

            SyncPingTrackers();
            ApplySettings(_settings);
            RestoreWindowPosition();

            // Initial poll
            _latestSnapshot = _metrics.GetSnapshot();
            _history.Add(_latestSnapshot);
            _trafficTracker.Update();

            // Fetch IP once at startup in background
            RefreshIpAddressesAsync();

            _updateTimer.Start();
        }

        private void InitializeTray()
        {
            _trayContextMenu = new ContextMenuStrip();
            ToolStripMenuItem itemShow = new ToolStripMenuItem("Показать / Скрыть", null, (s, e) => ToggleVisibility());
            itemShow.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            ToolStripMenuItem itemSettings = new ToolStripMenuItem("Настройки...", null, (s, e) => OpenSettings());
            ToolStripMenuItem itemExit = new ToolStripMenuItem("Выход", null, (s, e) => CleanExit());

            _trayContextMenu.Items.Add(itemShow);
            _trayContextMenu.Items.Add(new ToolStripSeparator());
            _trayContextMenu.Items.Add(itemSettings);
            _trayContextMenu.Items.Add(new ToolStripSeparator());
            _trayContextMenu.Items.Add(itemExit);

            _trayIcon = new NotifyIcon
            {
                Text = "Монитор ресурсов",
                Icon = CreateTrayIcon(),
                ContextMenuStrip = _trayContextMenu,
                Visible = true
            };

            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleVisibility();
                }
            };
        }

        private void InitializeContextMenu()
        {
            _contextMenu = new ContextMenuStrip();

            ToolStripMenuItem itemPin = new ToolStripMenuItem("Поверх всех окон", null, (s, e) =>
            {
                _settings.TopMost = !_settings.TopMost;
                this.TopMost = _settings.TopMost;
                _settings.Save();
                this.Invalidate();
            });

            ToolStripMenuItem itemGraph = new ToolStripMenuItem("Показывать график", null, (s, e) =>
            {
                _settings.ShowGraph = !_settings.ShowGraph;
                _settings.Save();
                this.Invalidate();
            });

            ToolStripMenuItem itemRefreshIp = new ToolStripMenuItem("Обновить IP адреса", null, (s, e) => RefreshIpAddressesAsync());
            ToolStripMenuItem itemSettings = new ToolStripMenuItem("Настройки...", null, (s, e) => OpenSettings());
            ToolStripMenuItem itemMin = new ToolStripMenuItem("Свернуть в трей", null, (s, e) => HideToTray());
            ToolStripMenuItem itemExit = new ToolStripMenuItem("Выход", null, (s, e) => CleanExit());

            _contextMenu.Opening += (s, e) =>
            {
                itemPin.Checked = _settings.TopMost;
                itemGraph.Checked = _settings.ShowGraph;
            };

            _contextMenu.Items.Add(itemPin);
            _contextMenu.Items.Add(itemGraph);
            _contextMenu.Items.Add(itemRefreshIp);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(itemSettings);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(itemMin);
            _contextMenu.Items.Add(itemExit);

            this.ContextMenuStrip = _contextMenu;
        }

        private void SyncPingTrackers()
        {
            List<PingTargetTracker> toKeep = new List<PingTargetTracker>();
            foreach (string host in _settings.PingTargets)
            {
                PingTargetTracker existing = _pingTrackers.Find(p => string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    toKeep.Add(existing);
                }
                else
                {
                    toKeep.Add(new PingTargetTracker(host));
                }
            }

            foreach (PingTargetTracker old in _pingTrackers)
            {
                if (!toKeep.Contains(old))
                {
                    old.Dispose();
                }
            }

            _pingTrackers.Clear();
            _pingTrackers.AddRange(toKeep);
        }

        public void ApplySettings(AppSettings s)
        {
            this.TopMost = s.TopMost;
            this.Opacity = s.Opacity;

            if (_updateTimer.Interval != s.UpdateIntervalMs)
            {
                _updateTimer.Interval = s.UpdateIntervalMs;
            }

            if (_processTracker != null)
            {
                _processTracker.ApplySettings(s);
            }

            if (_topProcForm != null && !_topProcForm.IsDisposed)
            {
                _topProcForm.TopMost = s.TopMost;
                _topProcForm.UpdateFormSize();
                _topProcForm.Invalidate();
            }

            SyncPingTrackers();
            PruneHistory();
            this.Invalidate();
        }

        public void RefreshIpAddressesAsync()
        {
            if (_isRefreshingIp) return;
            _isRefreshingIp = true;
            _localIp = "...";
            _externalIp = "...";
            this.Invalidate();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string loc = NetworkHelper.GetLocalIp();
                string ext = NetworkHelper.GetExternalIp();

                try
                {
                    this.BeginInvoke((MethodInvoker)(() =>
                    {
                        _localIp = loc;
                        _externalIp = ext;
                        _isRefreshingIp = false;
                        this.Invalidate();
                    }));
                }
                catch
                {
                    _isRefreshingIp = false;
                }
            });
        }

        private void RestoreWindowPosition()
        {
            int w = _settings.WindowWidth > 0 ? _settings.WindowWidth : 160;
            if (w < 140) w = 140;
            int h = _settings.WindowHeight > 0 ? _settings.WindowHeight : 440;
            this.Size = new Size(w, h);

            if (_settings.WindowX != -1 || _settings.WindowY != -1)
            {
                Rectangle target = new Rectangle(_settings.WindowX, _settings.WindowY, w, h);
                bool visibleOnAnyScreen = false;
                foreach (Screen scr in Screen.AllScreens)
                {
                    if (scr.WorkingArea.IntersectsWith(target))
                    {
                        visibleOnAnyScreen = true;
                        break;
                    }
                }

                if (visibleOnAnyScreen)
                {
                    this.StartPosition = FormStartPosition.Manual;
                    this.Location = new Point(_settings.WindowX, _settings.WindowY);
                    return;
                }
            }

            Rectangle primary = Screen.PrimaryScreen.WorkingArea;
            int x = primary.Right - w - 20;
            int y = primary.Top + 24;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(x, y);
        }

        private void SaveCurrentPosition()
        {
            if (this.WindowState == FormWindowState.Normal && this.Location.X > -10000 && this.Location.Y > -10000)
            {
                _settings.WindowX = this.Location.X;
                _settings.WindowY = this.Location.Y;
                _settings.WindowWidth = this.Size.Width;
                _settings.WindowHeight = this.Size.Height;
                _settings.Save();
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _latestSnapshot = _metrics.GetSnapshot();
            _history.Add(_latestSnapshot);
            PruneHistory();

            _trafficTracker.Update();

            for (int i = 0; i < _pingTrackers.Count; i++)
            {
                _pingTrackers[i].PingAsync();
            }

            this.Invalidate();
        }

        private void PruneHistory()
        {
            int maxPoints = Math.Max(10, (_settings.GraphPeriodSeconds * 1000) / Math.Max(200, _settings.UpdateIntervalMs));
            while (_history.Count > maxPoints)
            {
                _history.RemoveAt(0);
            }
        }

        public void OpenSettings()
        {
            _isDragging = false;
            SaveCurrentPosition();

            _isCloseHovered = false;
            _isMinHovered = false;
            _isSettingsHovered = false;
            _isRefreshIpHovered = false;
            this.Invalidate();

            using (SettingsForm form = new SettingsForm(this, _settings))
            {
                form.ShowDialog(this);
            }

            _isDragging = false;
            this.Activate();
            this.Focus();
        }

        public void ToggleTopProcessesWindow()
        {
            if (_topProcForm != null && !_topProcForm.IsDisposed && _topProcForm.Visible)
            {
                _topProcForm.Hide();
                return;
            }

            if (_topProcForm == null || _topProcForm.IsDisposed)
            {
                _topProcForm = new TopProcessesForm(_settings, _processTracker);
            }

            PositionTopProcessesForm();
            _topProcForm.Show();
            _topProcForm.BringToFront();
        }

        private void PositionTopProcessesForm()
        {
            if (_topProcForm == null) return;
            Screen screen = Screen.FromControl(this);
            if (screen == null) screen = Screen.PrimaryScreen;
            Rectangle work = screen.WorkingArea;

            int w = _topProcForm.Width;
            int h = _topProcForm.Height;
            int x;

            if (this.Right + w + 4 <= work.Right)
            {
                x = this.Right + 4;
            }
            else if (this.Left - w - 4 >= work.Left)
            {
                x = this.Left - w - 4;
            }
            else
            {
                x = Math.Max(work.Left, Math.Min(work.Right - w, this.Left));
            }

            int y = Math.Max(work.Top, Math.Min(work.Bottom - h, this.Top + 20));
            _topProcForm.Location = new Point(x, y);
        }

        public void ToggleVisibility()
        {
            if (this.Visible)
            {
                SaveCurrentPosition();
                this.Hide();
            }
            else
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            }
        }

        public void HideToTray()
        {
            SaveCurrentPosition();
            this.Hide();
        }

        public void CleanExit()
        {
            SaveCurrentPosition();

            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer.Dispose();
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            foreach (PingTargetTracker tracker in _pingTrackers)
            {
                tracker.Dispose();
            }

            if (_topProcForm != null && !_topProcForm.IsDisposed)
            {
                _topProcForm.Close();
                _topProcForm.Dispose();
                _topProcForm = null;
            }

            if (_processTracker != null)
            {
                _processTracker.Dispose();
            }

            if (_metrics != null)
            {
                _metrics.Dispose();
            }

            Application.Exit();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowPosition();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_settings.WindowX != -1 || _settings.WindowY != -1)
            {
                this.Location = new Point(_settings.WindowX, _settings.WindowY);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveCurrentPosition();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_EXITSIZEMOVE)
            {
                base.WndProc(ref m);
                SaveCurrentPosition();
                return;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                Point cursor = PointToClient(Cursor.Position);

                // Resize border tests
                if (cursor.X <= ResizeBorderWidth && cursor.Y <= ResizeBorderWidth) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (cursor.X >= ClientSize.Width - ResizeBorderWidth && cursor.Y <= ResizeBorderWidth) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (cursor.X <= ResizeBorderWidth && cursor.Y >= ClientSize.Height - ResizeBorderWidth) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (cursor.X >= ClientSize.Width - ResizeBorderWidth && cursor.Y >= ClientSize.Height - ResizeBorderWidth) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }

                if (cursor.X <= ResizeBorderWidth) { m.Result = (IntPtr)HTLEFT; return; }
                if (cursor.X >= ClientSize.Width - ResizeBorderWidth) { m.Result = (IntPtr)HTRIGHT; return; }
                if (cursor.Y <= ResizeBorderWidth) { m.Result = (IntPtr)HTTOP; return; }
                if (cursor.Y >= ClientSize.Height - ResizeBorderWidth) { m.Result = (IntPtr)HTBOTTOM; return; }

                // All client/caption clicks handled via client mouse events
                m.Result = (IntPtr)HTCLIENT;
                return;
            }

            // Screen edge snapping
            if (_settings.SnapToEdges && m.Msg == WM_MOVING)
            {
                RECT rect = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                Point center = new Point(rect.Left + width / 2, rect.Top + height / 2);
                Screen screen = Screen.FromPoint(center);
                if (screen == null) screen = Screen.PrimaryScreen;
                Rectangle work = screen.WorkingArea;

                int snapDist = _settings.SnapDistance;

                if (Math.Abs(rect.Left - work.Left) <= snapDist)
                {
                    rect.Left = work.Left;
                    rect.Right = work.Left + width;
                }
                else if (Math.Abs(rect.Right - work.Right) <= snapDist)
                {
                    rect.Right = work.Right;
                    rect.Left = work.Right - width;
                }

                if (Math.Abs(rect.Top - work.Top) <= snapDist)
                {
                    rect.Top = work.Top;
                    rect.Bottom = work.Top + height;
                }
                else if (Math.Abs(rect.Bottom - work.Bottom) <= snapDist)
                {
                    rect.Bottom = work.Bottom;
                    rect.Top = work.Bottom - height;
                }

                Marshal.StructureToPtr(rect, m.LParam, true);
                return;
            }

            base.WndProc(ref m);
        }

        private Point ApplySnap(Point targetLoc, Size formSize)
        {
            int snapDist = _settings.SnapDistance;
            int x = targetLoc.X;
            int y = targetLoc.Y;
            int w = formSize.Width;
            int h = formSize.Height;

            Point center = new Point(x + w / 2, y + h / 2);
            Screen screen = Screen.FromPoint(center);
            if (screen == null) screen = Screen.PrimaryScreen;
            Rectangle work = screen.WorkingArea;

            if (Math.Abs(x - work.Left) <= snapDist)
            {
                x = work.Left;
            }
            else if (Math.Abs((x + w) - work.Right) <= snapDist)
            {
                x = work.Right - w;
            }

            if (Math.Abs(y - work.Top) <= snapDist)
            {
                y = work.Top;
            }
            else if (Math.Abs((y + h) - work.Bottom) <= snapDist)
            {
                y = work.Bottom - h;
            }

            return new Point(x, y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                // If clicked on any button, do NOT initiate drag
                if (_rectBtnClose.Contains(e.Location) ||
                    _rectBtnMin.Contains(e.Location) ||
                    _rectBtnSettings.Contains(e.Location) ||
                    _rectBtnRefreshIp.Contains(e.Location) ||
                    (_rectBtnLightning != Rectangle.Empty && _rectBtnLightning.Contains(e.Location)))
                {
                    return;
                }

                // If on resize borders, do NOT initiate drag
                if (e.X <= ResizeBorderWidth || e.X >= this.ClientSize.Width - ResizeBorderWidth ||
                    e.Y <= ResizeBorderWidth || e.Y >= this.ClientSize.Height - ResizeBorderWidth)
                {
                    return;
                }

                // Direct client mouse dragging
                _isDragging = true;
                _dragStartPoint = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isDragging)
            {
                int dx = e.X - _dragStartPoint.X;
                int dy = e.Y - _dragStartPoint.Y;

                if (dx != 0 || dy != 0)
                {
                    Point target = new Point(this.Location.X + dx, this.Location.Y + dy);
                    if (_settings.SnapToEdges)
                    {
                        target = ApplySnap(target, this.Size);
                    }
                    this.Location = target;
                }
                return;
            }

            bool prevClose = _isCloseHovered;
            bool prevMin = _isMinHovered;
            bool prevSettings = _isSettingsHovered;
            bool prevRefresh = _isRefreshIpHovered;
            bool prevLightning = _isLightningHovered;

            _isCloseHovered = _rectBtnClose.Contains(e.Location);
            _isMinHovered = _rectBtnMin.Contains(e.Location);
            _isSettingsHovered = _rectBtnSettings.Contains(e.Location);
            _isRefreshIpHovered = _rectBtnRefreshIp.Contains(e.Location);
            _isLightningHovered = _rectBtnLightning != Rectangle.Empty && _rectBtnLightning.Contains(e.Location);

            if (_isCloseHovered || _isMinHovered || _isSettingsHovered || _isRefreshIpHovered || _isLightningHovered)
            {
                this.Cursor = Cursors.Hand;
            }
            else
            {
                this.Cursor = Cursors.Default;
            }

            if (prevClose != _isCloseHovered || prevMin != _isMinHovered ||
                prevSettings != _isSettingsHovered || prevRefresh != _isRefreshIpHovered ||
                prevLightning != _isLightningHovered)
            {
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                SaveCurrentPosition();
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_isDragging)
            {
                _isDragging = false;
                SaveCurrentPosition();
            }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (this.WindowState == FormWindowState.Normal && this.Location.X > -10000 && this.Location.Y > -10000)
            {
                _settings.WindowX = this.Location.X;
                _settings.WindowY = this.Location.Y;
            }
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            SaveCurrentPosition();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayoutRectangles();
            this.Invalidate();
        }

        private void UpdateLayoutRectangles()
        {
            int btnSize = 17;
            int top = 3;
            int right = this.ClientSize.Width - 3;

            _rectBtnClose = new Rectangle(right - btnSize, top, btnSize, btnSize);
            _rectBtnMin = new Rectangle(right - btnSize * 2 - 2, top, btnSize, btnSize);
            _rectBtnSettings = new Rectangle(right - btnSize * 3 - 3, top, btnSize, btnSize);

            // IP Refresh button
            int ipH = GetBottomIpHeight();
            int ipY = this.ClientSize.Height - ipH;
            _rectBtnRefreshIp = new Rectangle(this.ClientSize.Width - 20, ipY + (ipH - 18) / 2, 16, 16);
        }

        private int GetBottomIpHeight()
        {
            // If window width < 180, use 2-line layout (32px) so text never breaks or clips
            return this.ClientSize.Width < 180 ? 32 : 24;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isCloseHovered = false;
            _isMinHovered = false;
            _isSettingsHovered = false;
            _isRefreshIpHovered = false;
            _isLightningHovered = false;
            this.Cursor = Cursors.Default;
            this.Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button == MouseButtons.Left)
            {
                if (e.Y <= HeaderHeight)
                {
                    if (_rectBtnClose.Contains(e.Location)) { CleanExit(); return; }
                    if (_rectBtnMin.Contains(e.Location)) { HideToTray(); return; }
                    if (_rectBtnSettings.Contains(e.Location)) { OpenSettings(); return; }
                }

                if (_rectBtnLightning != Rectangle.Empty && _rectBtnLightning.Contains(e.Location))
                {
                    ToggleTopProcessesWindow();
                    return;
                }

                if (_rectBtnRefreshIp.Contains(e.Location))
                {
                    RefreshIpAddressesAsync();
                    return;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            bool isNarrow = w < 180;

            Color colCpu = _settings.GetCpuColor();
            Color colGpu = _settings.GetGpuColor();
            Color colRam = _settings.GetRamColor();
            Color colPing = _settings.GetPingColor();
            Color colNetDown = _settings.GetNetDownColor();
            Color colNetUp = _settings.GetNetUpColor();
            Color colText = _settings.GetTextColor();
            Color colTextDim = Color.FromArgb(166, 173, 200);

            // 1. Background & Border
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(24, 24, 37)))
            {
                g.FillRectangle(bgBrush, 0, 0, w, h);
            }
            using (Pen borderPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }

            // 2. Header Bar
            using (SolidBrush headerBrush = new SolidBrush(Color.FromArgb(17, 17, 27)))
            {
                g.FillRectangle(headerBrush, 1, 1, w - 2, HeaderHeight - 1);
            }
            using (Pen sepPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            {
                g.DrawLine(sepPen, 1, HeaderHeight, w - 2, HeaderHeight);
            }

            using (Font titleFont = new Font("Segoe UI", isNarrow ? 7.5F : 8.5F, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(colText))
            {
                string title = isNarrow ? "МОН" : "МОНИТОР";
                g.DrawString(title, titleFont, titleBrush, 6, 4);
            }

            DrawHeaderButton(g, _rectBtnSettings, "⚙", _isSettingsHovered, colTextDim);
            DrawHeaderButton(g, _rectBtnMin, "—", _isMinHovered, colTextDim);
            DrawHeaderButton(g, _rectBtnClose, "✕", _isCloseHovered, _isCloseHovered ? Color.FromArgb(243, 139, 168) : colTextDim);

            // 3. Content Metrics Area
            MetricSnapshot snap = _latestSnapshot ?? new MetricSnapshot();
            int contentY = HeaderHeight + 5;
            int pad = 6;
            int barWidth = w - (pad * 2);

            using (Font boldFont = new Font("Segoe UI", 8F, FontStyle.Bold))
            using (Font regFont = new Font("Segoe UI", 8F, FontStyle.Regular))
            using (Font miniFont = new Font("Segoe UI", 6.5F, FontStyle.Regular))
            using (Font microFont = new Font("Segoe UI", 5F, FontStyle.Regular))
            using (SolidBrush textCpuBrush = new SolidBrush(colCpu))
            using (SolidBrush textGpuBrush = new SolidBrush(colGpu))
            using (SolidBrush textRamBrush = new SolidBrush(colRam))
            using (SolidBrush textPingBrush = new SolidBrush(colPing))
            using (SolidBrush textDimBrush = new SolidBrush(colTextDim))
            using (SolidBrush textMainBrush = new SolidBrush(colText))
            using (SolidBrush barBgBrush = new SolidBrush(Color.FromArgb(49, 50, 68)))
            {
                // Requirement: Per-Core Bars in ONE SINGLE COLUMN, 3x closer vertically, WITHOUT NUMBER LABELS
                if (_settings.ShowCoreBars && snap.CorePercents != null && snap.CorePercents.Length > 0)
                {
                    int coreCount = snap.CorePercents.Length;
                    int rowH = 3;     // 3x closer vertically
                    int coreBarH = 2; // Sleek 2px bar

                    for (int i = 0; i < coreCount; i++)
                    {
                        int cy = contentY + (i * rowH);
                        DrawProgressBar(g, barBgBrush, textCpuBrush, pad, cy, barWidth, coreBarH, snap.CorePercents[i]);
                    }

                    contentY += (coreCount * rowH) + 4;
                }

                // Requirement: "Надпись CPU поставь над общей шкалой загрузки процессора"
                g.DrawString("CPU", boldFont, textCpuBrush, pad, contentY);
                SizeF cpuLblSize = g.MeasureString("CPU", boldFont);

                if (_settings.ShowTopProcessesButton)
                {
                    int boltX = pad + (int)cpuLblSize.Width + 1;
                    int boltY = contentY;
                    _rectBtnLightning = new Rectangle(boltX, boltY, 13, 13);

                    if (_isLightningHovered)
                    {
                        using (SolidBrush hoverBg = new SolidBrush(Color.FromArgb(49, 50, 68)))
                        {
                            g.FillRectangle(hoverBg, _rectBtnLightning);
                        }
                    }

                    PointF[] bolt = new PointF[]
                    {
                        new PointF(boltX + 7.5f, boltY + 1f),
                        new PointF(boltX + 3.0f, boltY + 6.5f),
                        new PointF(boltX + 6.5f, boltY + 6.5f),
                        new PointF(boltX + 4.5f, boltY + 12f),
                        new PointF(boltX + 10.5f, boltY + 5.5f),
                        new PointF(boltX + 7.0f, boltY + 5.5f)
                    };
                    Color boltCol = _isLightningHovered ? Color.FromArgb(250, 204, 21) : Color.FromArgb(234, 179, 8);
                    using (SolidBrush boltBrush = new SolidBrush(boltCol))
                    {
                        g.FillPolygon(boltBrush, bolt);
                    }
                }
                else
                {
                    _rectBtnLightning = Rectangle.Empty;
                }

                string cpuText = isNarrow ? string.Format("{0:0}%", snap.CpuPercent) : string.Format("{0:0.0}%", snap.CpuPercent);
                SizeF cpuSize = g.MeasureString(cpuText, regFont);
                g.DrawString(cpuText, regFont, textDimBrush, w - pad - cpuSize.Width, contentY);
                contentY += 15;

                // General CPU Bar
                DrawProgressBar(g, barBgBrush, textCpuBrush, pad, contentY, barWidth, 5, snap.CpuPercent);
                contentY += 10;

                // Requirement 4: GPU Load Bar
                g.DrawString("GPU", boldFont, textGpuBrush, pad, contentY);
                string gpuText = string.Format("{0:0}%", snap.GpuPercent);
                SizeF gpuSize = g.MeasureString(gpuText, regFont);
                g.DrawString(gpuText, regFont, textDimBrush, w - pad - gpuSize.Width, contentY);
                contentY += 15;

                DrawProgressBar(g, barBgBrush, textGpuBrush, pad, contentY, barWidth, 5, snap.GpuPercent);
                contentY += 10;

                // RAM Header & Bar
                g.DrawString("RAM", boldFont, textRamBrush, pad, contentY);
                string ramText = isNarrow ? string.Format("{0:0}%", snap.RamPercent) : string.Format("{0:0}% ({1:0}/{2:0} ГБ)", snap.RamPercent, snap.RamUsedGb, snap.RamTotalGb);
                SizeF ramSize = g.MeasureString(ramText, regFont);
                g.DrawString(ramText, regFont, textDimBrush, w - pad - ramSize.Width, contentY);
                contentY += 15;

                DrawProgressBar(g, barBgBrush, textRamBrush, pad, contentY, barWidth, 5, snap.RamPercent);
                contentY += 12;

                // CPU/RAM Load Chart (if enabled)
                if (_settings.ShowGraph)
                {
                    int chartH = 36;
                    DrawCpuRamChart(g, pad, contentY, barWidth, chartH, colCpu, colRam);
                    contentY += chartH + 10;
                }

                // Ping Monitors Section
                for (int i = 0; i < _pingTrackers.Count; i++)
                {
                    PingTargetTracker pt = _pingTrackers[i];

                    // Line 1: Target host on left, Current Ping on right
                    string hostLabel = pt.Host;
                    if (isNarrow && hostLabel.Length > 10) hostLabel = hostLabel.Substring(0, 9) + "..";
                    g.DrawString(hostLabel, boldFont, textPingBrush, pad, contentY);

                    string currentPingText = pt.LastPing >= 0 ? pt.LastPing + " ms" : (pt.IsTimeout ? "Таймаут" : "— ms");
                    SizeF curPingSize = g.MeasureString(currentPingText, boldFont);
                    g.DrawString(currentPingText, boldFont, textPingBrush, w - pad - curPingSize.Width, contentY);
                    contentY += 14;

                    // Line 2: Requirement - "Верни отображение минимального, среднего и максимального пинга"
                    string minStr = pt.MinPing >= 0 ? pt.MinPing.ToString() : "-";
                    string avgStr = pt.AvgPing >= 0 ? pt.AvgPing.ToString() : "-";
                    string maxStr = pt.MaxPing >= 0 ? pt.MaxPing.ToString() : "-";
                    string statsText = isNarrow
                        ? string.Format("М:{0} С:{1} М:{2}", minStr, avgStr, maxStr)
                        : string.Format("Мин: {0}ms  Ср: {1}ms  Макс: {2}ms", minStr, avgStr, maxStr);
                    g.DrawString(statsText, miniFont, textDimBrush, pad, contentY);
                    contentY += 13;

                    // Ping Chart
                    int pingChartH = 30;
                    DrawPingChart(g, pad, contentY, barWidth, pingChartH, pt.GetHistorySnapshot(), colPing);
                    contentY += pingChartH + 8;
                }

                // Requirement 5: Network Traffic Section (Placed above the IP section)
                int ipSectionH = GetBottomIpHeight();
                int netSectionH = 50; // header (14px) + chart (34px) + gap
                int netY = h - ipSectionH - netSectionH - 2;

                if (netY > contentY)
                {
                    contentY = netY;
                }

                DrawNetworkTrafficSection(g, pad, contentY, barWidth, isNarrow, colNetDown, colNetUp, colTextDim);
            }

            // Bottom IP Section
            DrawBottomIpSection(g, w, h, colText, colTextDim, isNarrow);

            // Corner grip
            using (SolidBrush gripBrush = new SolidBrush(Color.FromArgb(69, 71, 90)))
            {
                g.FillRectangle(gripBrush, w - 3, h - 3, 2, 2);
                g.FillRectangle(gripBrush, w - 6, h - 3, 2, 2);
                g.FillRectangle(gripBrush, w - 3, h - 6, 2, 2);
            }
        }

        private void DrawNetworkTrafficSection(Graphics g, int x, int y, int width, bool isNarrow, Color colDown, Color colUp, Color colDim)
        {
            using (Font miniBold = new Font("Segoe UI", 7F, FontStyle.Bold))
            using (Font tagFont = new Font("Segoe UI", 6.5F, FontStyle.Regular))
            using (SolidBrush downBrush = new SolidBrush(colDown))
            using (SolidBrush upBrush = new SolidBrush(colUp))
            using (SolidBrush dimBrush = new SolidBrush(colDim))
            {
                // Format speeds
                string downSpeedStr = NetworkTrafficTracker.FormatSpeed(_trafficTracker.CurrentDownloadSpeed, isNarrow);
                string upSpeedStr = NetworkTrafficTracker.FormatSpeed(_trafficTracker.CurrentUploadSpeed, isNarrow);

                // Speeds Header
                string leftTag = isNarrow ? "↓" : "СЕТЬ ↓";
                g.DrawString(leftTag, miniBold, downBrush, x, y);
                SizeF leftTagSz = g.MeasureString(leftTag, miniBold);
                g.DrawString(downSpeedStr, miniBold, downBrush, x + leftTagSz.Width + 1, y);

                string upLabel = "↑ " + upSpeedStr;
                SizeF upLabelSz = g.MeasureString(upLabel, miniBold);
                g.DrawString(upLabel, miniBold, upBrush, x + width - upLabelSz.Width, y);

                int chartY = y + 14;
                int chartH = 32;

                // Auto-scaling Network Traffic Chart
                using (SolidBrush graphBg = new SolidBrush(Color.FromArgb(17, 17, 27)))
                using (Pen borderPen = new Pen(Color.FromArgb(49, 50, 68), 1))
                using (Pen gridPen = new Pen(Color.FromArgb(20, 255, 255, 255), 1))
                {
                    g.FillRectangle(graphBg, x, chartY, width, chartH);
                    g.DrawRectangle(borderPen, x, chartY, width, chartH);

                    gridPen.DashStyle = DashStyle.Dot;
                    g.DrawLine(gridPen, x + 1, chartY + chartH / 2, x + width - 1, chartY + chartH / 2);

                    // Dynamic scale ceiling tag
                    string ceilTag = NetworkTrafficTracker.FormatSpeed(_trafficTracker.AutoScaleMaxBytesPerSec, true);
                    g.DrawString(ceilTag, tagFont, dimBrush, x + 2, chartY + 1);

                    double[] dHistory, uHistory;
                    _trafficTracker.GetHistorySnapshots(out dHistory, out uHistory);

                    if (dHistory.Length >= 2)
                    {
                        int count = dHistory.Length;
                        double maxCeil = Math.Max(1024, _trafficTracker.AutoScaleMaxBytesPerSec);
                        float stepX = (float)width / Math.Max(1, count - 1);

                        PointF[] dPoints = new PointF[count];
                        PointF[] uPoints = new PointF[count];

                        for (int i = 0; i < count; i++)
                        {
                            float px = x + (i * stepX);
                            float pyD = chartY + chartH - (float)(chartH * Math.Min(maxCeil, dHistory[i]) / maxCeil);
                            float pyU = chartY + chartH - (float)(chartH * Math.Min(maxCeil, uHistory[i]) / maxCeil);
                            dPoints[i] = new PointF(px, pyD);
                            uPoints[i] = new PointF(px, pyU);
                        }

                        // Download area fill
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddLine(new PointF(dPoints[0].X, chartY + chartH), dPoints[0]);
                            for (int i = 1; i < count; i++) path.AddLine(dPoints[i - 1], dPoints[i]);
                            path.AddLine(dPoints[count - 1], new PointF(dPoints[count - 1].X, chartY + chartH));
                            path.CloseFigure();
                            using (SolidBrush fill = new SolidBrush(Color.FromArgb(30, colDown.R, colDown.G, colDown.B)))
                            {
                                g.FillPath(fill, path);
                            }
                        }

                        // Download & Upload lines
                        using (Pen pDown = new Pen(colDown, 1.2f))
                        using (Pen pUp = new Pen(colUp, 1.2f))
                        {
                            g.DrawLines(pDown, dPoints);
                            g.DrawLines(pUp, uPoints);
                        }
                    }
                }
            }
        }

        private void DrawBottomIpSection(Graphics g, int w, int h, Color textCol, Color dimCol, bool isNarrow)
        {
            int ipH = GetBottomIpHeight();
            int ipY = h - ipH;

            using (Pen sepPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            {
                g.DrawLine(sepPen, 1, ipY, w - 2, ipY);
            }

            using (SolidBrush barBg = new SolidBrush(Color.FromArgb(17, 17, 27)))
            {
                g.FillRectangle(barBg, 1, ipY + 1, w - 2, ipH - 2);
            }

            using (Font ipFont = new Font("Segoe UI", 6.5F, FontStyle.Regular))
            using (SolidBrush textBrush = new SolidBrush(textCol))
            using (SolidBrush dimBrush = new SolidBrush(dimCol))
            {
                if (isNarrow)
                {
                    // 2-line layout so addresses never clip or overlap in 140px width!
                    g.DrawString("IP: " + _localIp, ipFont, dimBrush, 6, ipY + 2);
                    g.DrawString("eIP: " + _externalIp, ipFont, textBrush, 6, ipY + 16);
                }
                else
                {
                    // 1-line layout when width >= 180px
                    string locStr = "IP: " + _localIp;
                    string extStr = "eIP: " + _externalIp;
                    g.DrawString(locStr, ipFont, dimBrush, 6, ipY + 6);
                    SizeF locSz = g.MeasureString(locStr, ipFont);
                    g.DrawString(extStr, ipFont, textBrush, 6 + locSz.Width + 8, ipY + 6);
                }
            }

            // Refresh button
            if (_isRefreshIpHovered)
            {
                using (SolidBrush hoverBg = new SolidBrush(Color.FromArgb(49, 50, 68)))
                {
                    g.FillRectangle(hoverBg, _rectBtnRefreshIp);
                }
            }

            using (Font btnFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (SolidBrush btnBrush = new SolidBrush(_isRefreshingIp ? Color.FromArgb(56, 189, 248) : dimCol))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("⟳", btnFont, btnBrush, _rectBtnRefreshIp, sf);
            }
        }

        private void DrawProgressBar(Graphics g, Brush bgBrush, Brush fillBrush, int x, int y, int width, int height, double percent)
        {
            g.FillRectangle(bgBrush, x, y, width, height);
            if (percent > 0)
            {
                int fillW = (int)Math.Round((width * Math.Min(100.0, Math.Max(0.0, percent))) / 100.0);
                if (fillW < 2 && percent > 0) fillW = 2;
                g.FillRectangle(fillBrush, x, y, fillW, height);
            }
        }

        private void DrawCpuRamChart(Graphics g, int x, int y, int width, int height, Color cpuCol, Color ramCol)
        {
            using (SolidBrush graphBg = new SolidBrush(Color.FromArgb(17, 17, 27)))
            using (Pen borderPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            using (Pen gridPen = new Pen(Color.FromArgb(20, 255, 255, 255), 1))
            {
                g.FillRectangle(graphBg, x, y, width, height);
                g.DrawRectangle(borderPen, x, y, width, height);

                gridPen.DashStyle = DashStyle.Dot;
                g.DrawLine(gridPen, x + 1, y + height / 2, x + width - 1, y + height / 2);

                if (_history.Count < 2) return;

                int count = _history.Count;
                PointF[] cpuPoints = new PointF[count];
                PointF[] ramPoints = new PointF[count];
                float stepX = (float)width / Math.Max(1, count - 1);

                for (int i = 0; i < count; i++)
                {
                    float px = x + (i * stepX);
                    double cpu = Math.Min(100.0, Math.Max(0.0, _history[i].CpuPercent));
                    double ram = Math.Min(100.0, Math.Max(0.0, _history[i].RamPercent));

                    float pyCpu = y + height - (float)(height * cpu / 100.0);
                    float pyRam = y + height - (float)(height * ram / 100.0);

                    cpuPoints[i] = new PointF(px, pyCpu);
                    ramPoints[i] = new PointF(px, pyRam);
                }

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddLine(new PointF(cpuPoints[0].X, y + height), cpuPoints[0]);
                    for (int i = 1; i < count; i++) path.AddLine(cpuPoints[i - 1], cpuPoints[i]);
                    path.AddLine(cpuPoints[count - 1], new PointF(cpuPoints[count - 1].X, y + height));
                    path.CloseFigure();

                    using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(30, cpuCol.R, cpuCol.G, cpuCol.B)))
                    {
                        g.FillPath(fillBrush, path);
                    }
                }

                using (Pen cpuPen = new Pen(cpuCol, 1.2f))
                using (Pen ramPen = new Pen(ramCol, 1.2f))
                {
                    g.DrawLines(cpuPen, cpuPoints);
                    g.DrawLines(ramPen, ramPoints);
                }
            }
        }

        private void DrawPingChart(Graphics g, int x, int y, int width, int height, int[] pingHistory, Color pingCol)
        {
            using (SolidBrush graphBg = new SolidBrush(Color.FromArgb(17, 17, 27)))
            using (Pen borderPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            using (Pen gridPen = new Pen(Color.FromArgb(20, 255, 255, 255), 1))
            {
                g.FillRectangle(graphBg, x, y, width, height);
                g.DrawRectangle(borderPen, x, y, width, height);

                gridPen.DashStyle = DashStyle.Dot;
                g.DrawLine(gridPen, x + 1, y + height / 2, x + width - 1, y + height / 2);

                if (pingHistory == null || pingHistory.Length < 2) return;

                int count = pingHistory.Length;
                PointF[] points = new PointF[count];
                float stepX = (float)width / Math.Max(1, count - 1);

                int maxVal = 50;
                for (int i = 0; i < count; i++)
                {
                    if (pingHistory[i] > maxVal) maxVal = pingHistory[i];
                }
                maxVal = (int)Math.Ceiling(maxVal * 1.2 / 10.0) * 10;
                if (maxVal < 20) maxVal = 20;

                for (int i = 0; i < count; i++)
                {
                    float px = x + (i * stepX);
                    int val = pingHistory[i];
                    float py = val > 0 ? (y + height - (float)(height * Math.Min(maxVal, val) / maxVal)) : (y + height);
                    points[i] = new PointF(px, py);
                }

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddLine(new PointF(points[0].X, y + height), points[0]);
                    for (int i = 1; i < count; i++) path.AddLine(points[i - 1], points[i]);
                    path.AddLine(points[count - 1], new PointF(points[count - 1].X, y + height));
                    path.CloseFigure();

                    using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(25, pingCol.R, pingCol.G, pingCol.B)))
                    {
                        g.FillPath(fillBrush, path);
                    }
                }

                using (Pen pingPen = new Pen(pingCol, 1.2f))
                {
                    g.DrawLines(pingPen, points);
                }
            }
        }

        private void DrawHeaderButton(Graphics g, Rectangle rect, string text, bool isHovered, Color foreColor)
        {
            if (isHovered)
            {
                using (SolidBrush hoverBg = new SolidBrush(Color.FromArgb(49, 50, 68)))
                {
                    g.FillRectangle(hoverBg, rect);
                }
            }

            using (Font btnFont = new Font("Segoe UI", 7.5F, FontStyle.Regular))
            using (SolidBrush textBrush = new SolidBrush(foreColor))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(text, btnFont, textBrush, rect, sf);
            }
        }

        private static Icon CreateTrayIcon()
        {
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) return exeIcon;
            }
            catch { }

            using (Bitmap bmp = new Bitmap(16, 16))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(30, 30, 46)))
                using (Pen borderPen = new Pen(Color.FromArgb(56, 189, 248), 1))
                {
                    g.FillRectangle(bodyBrush, 1, 1, 13, 10);
                    g.DrawRectangle(borderPen, 1, 1, 13, 10);
                }

                using (Pen linePen = new Pen(Color.FromArgb(192, 132, 252), 1))
                {
                    g.DrawLine(linePen, 3, 6, 6, 3);
                    g.DrawLine(linePen, 6, 3, 9, 8);
                    g.DrawLine(linePen, 9, 8, 12, 6);
                }

                using (SolidBrush standBrush = new SolidBrush(Color.FromArgb(166, 173, 200)))
                {
                    g.FillRectangle(standBrush, 6, 12, 4, 2);
                    g.FillRectangle(standBrush, 4, 14, 8, 1);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
    }
}
