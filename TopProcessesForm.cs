using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskMonitor
{
    public class TopProcessesForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly AppSettings _settings;
        private readonly ProcessCpuTracker _tracker;
        private readonly Timer _uiTimer;

        private Rectangle _rectHeader;
        private Rectangle _rectBtnClose;
        private Rectangle _rectBtnRefresh;
        private bool _isCloseHovered = false;
        private bool _isRefreshHovered = false;

        public TopProcessesForm(AppSettings settings, ProcessCpuTracker tracker)
        {
            _settings = settings;
            _tracker = tracker;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.BackColor = Color.FromArgb(24, 24, 37);

            UpdateFormSize();

            _uiTimer = new Timer();
            _uiTimer.Interval = 1000;
            _uiTimer.Tick += (s, e) => { if (this.Visible) this.Invalidate(); };

            this.VisibleChanged += (s, e) =>
            {
                if (this.Visible)
                {
                    this.TopMost = _settings.TopMost;
                    UpdateFormSize();
                    _uiTimer.Start();
                    this.Invalidate();
                }
                else
                {
                    _uiTimer.Stop();
                }
            };
        }

        public void UpdateFormSize()
        {
            int count = Math.Max(3, Math.Min(15, _settings.TopProcessesCount));
            int w = 310;
            int h = 32 /* Header */ + 24 /* Col Header */ + (count * 28) /* Rows */ + 24 /* Footer */ + 6;
            this.Size = new Size(w, h);
            _rectHeader = new Rectangle(0, 0, w, 32);
            _rectBtnClose = new Rectangle(w - 28, 4, 24, 24);
            _rectBtnRefresh = new Rectangle(w - 56, 4, 24, 24);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                this.Hide();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                if (_rectBtnClose.Contains(e.Location))
                {
                    this.Hide();
                    return;
                }
                if (_rectBtnRefresh.Contains(e.Location))
                {
                    this.Invalidate();
                    return;
                }
                if (_rectHeader.Contains(e.Location))
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool closeHov = _rectBtnClose.Contains(e.Location);
            bool refHov = _rectBtnRefresh.Contains(e.Location);

            if (closeHov != _isCloseHovered || refHov != _isRefreshHovered)
            {
                _isCloseHovered = closeHov;
                _isRefreshHovered = refHov;
                this.Invalidate();
            }

            if (closeHov || refHov)
            {
                this.Cursor = Cursors.Hand;
            }
            else
            {
                this.Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_isCloseHovered || _isRefreshHovered)
            {
                _isCloseHovered = false;
                _isRefreshHovered = false;
                this.Invalidate();
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

            // Background & Border
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(24, 24, 37)))
            {
                g.FillRectangle(bgBrush, 0, 0, w, h);
            }
            using (Pen borderPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }

            // Header Background
            using (SolidBrush headerBg = new SolidBrush(Color.FromArgb(17, 17, 27)))
            {
                g.FillRectangle(headerBg, 1, 1, w - 2, 31);
            }
            using (Pen sepPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            {
                g.DrawLine(sepPen, 0, 32, w, 32);
            }

            // Lightning Icon in Header
            PointF[] bolt = new PointF[]
            {
                new PointF(15f, 7f),
                new PointF(9f, 15f),
                new PointF(14f, 15f),
                new PointF(11f, 24f),
                new PointF(19f, 14f),
                new PointF(14f, 14f)
            };
            using (SolidBrush boltBrush = new SolidBrush(Color.FromArgb(250, 204, 21)))
            {
                g.FillPolygon(boltBrush, bolt);
            }

            // Header Title
            using (Font titleFont = new Font("Segoe UI", 9.5F, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(_settings.GetTextColor()))
            {
                g.DrawString("Топ процессов (CPU)", titleFont, titleBrush, 24, 7);
            }

            // Window Period Badge
            string badgeText = string.Format("{0} мин", _settings.TopProcessesWindowMinutes);
            using (Font badgeFont = new Font("Segoe UI", 7.5F, FontStyle.Regular))
            using (SolidBrush badgeBg = new SolidBrush(Color.FromArgb(49, 50, 68)))
            using (SolidBrush badgeTextBrush = new SolidBrush(Color.FromArgb(250, 204, 21)))
            {
                SizeF bSize = g.MeasureString(badgeText, badgeFont);
                int bx = 168;
                int by = 8;
                g.FillRectangle(badgeBg, bx, by, bSize.Width + 8, 16);
                g.DrawString(badgeText, badgeFont, badgeTextBrush, bx + 4, by + 1);
            }

            // Refresh Button
            DrawHeaderButton(g, _rectBtnRefresh, "⟳", _isRefreshHovered, _isRefreshHovered ? Color.FromArgb(56, 189, 248) : Color.FromArgb(166, 173, 200));

            // Close Button
            DrawHeaderButton(g, _rectBtnClose, "✕", _isCloseHovered, _isCloseHovered ? Color.FromArgb(243, 139, 168) : Color.FromArgb(166, 173, 200));

            // Column Header
            int colY = 36;
            using (Font colFont = new Font("Segoe UI", 7.5F, FontStyle.Bold))
            using (SolidBrush colBrush = new SolidBrush(Color.FromArgb(140, 145, 170)))
            {
                g.DrawString("#", colFont, colBrush, 12, colY);
                g.DrawString("Процесс", colFont, colBrush, 32, colY);
                g.DrawString("CPU %", colFont, colBrush, 175, colY);
                g.DrawString("Время", colFont, colBrush, 252, colY);
            }

            using (Pen sepPen = new Pen(Color.FromArgb(35, 36, 50), 1))
            {
                g.DrawLine(sepPen, 10, colY + 16, w - 10, colY + 16);
            }

            // Top Processes Rows
            int rowY = colY + 20;
            List<TopProcessItem> items = _tracker != null ? _tracker.GetTopProcesses(_settings.TopProcessesCount, _settings.TopProcessesWindowMinutes, _settings.TopProcessesIncludeExited) : new List<TopProcessItem>();

            Color cpuColor = _settings.GetCpuColor();
            Color textColor = _settings.GetTextColor();
            Color dimColor = Color.FromArgb(166, 173, 200);

            using (Font rankFont = new Font("Segoe UI", 8F, FontStyle.Bold))
            using (Font procFont = new Font("Segoe UI", 8.5F, FontStyle.Regular))
            using (Font smallFont = new Font("Segoe UI", 7F, FontStyle.Regular))
            using (Font valFont = new Font("Segoe UI", 8F, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (SolidBrush dimBrush = new SolidBrush(dimColor))
            using (SolidBrush barBgBrush = new SolidBrush(Color.FromArgb(49, 50, 68)))
            using (SolidBrush barCpuBrush = new SolidBrush(cpuColor))
            using (SolidBrush goldBrush = new SolidBrush(Color.FromArgb(250, 204, 21)))
            {
                if (items.Count == 0)
                {
                    g.DrawString("Сбор данных телеметрии процессов...", procFont, dimBrush, 32, rowY + 15);
                }
                else
                {
                    // Find max CPU for relative bar scale
                    double maxCpu = 0.1;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].CpuPercent > maxCpu) maxCpu = items[i].CpuPercent;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        TopProcessItem item = items[i];

                        // Rank
                        Brush rBrush = (i == 0) ? goldBrush : dimBrush;
                        g.DrawString((i + 1).ToString(), rankFont, rBrush, 12, rowY + 2);

                        // Process Name (truncated if too long)
                        string displayName = item.Name;
                        if (displayName.Length > 16) displayName = displayName.Substring(0, 15) + "…";

                        Brush pBrush = item.IsExited ? dimBrush : textBrush;
                        g.DrawString(displayName, procFont, pBrush, 32, rowY + 1);

                        // Mini indicator if exited
                        if (item.IsExited)
                        {
                            g.DrawString("закр.", smallFont, dimBrush, 138, rowY + 3);
                        }

                        // Mini Bar + Percent
                        int barX = 175;
                        int barW = 34;
                        int barH = 5;
                        int fillW = (int)Math.Round((item.CpuPercent / maxCpu) * barW);
                        if (fillW < 1 && item.CpuPercent > 0) fillW = 1;
                        if (fillW > barW) fillW = barW;

                        g.FillRectangle(barBgBrush, barX, rowY + 7, barW, barH);
                        if (fillW > 0)
                        {
                            g.FillRectangle(barCpuBrush, barX, rowY + 7, fillW, barH);
                        }

                        string pctText = string.Format("{0:0.0}%", item.CpuPercent);
                        g.DrawString(pctText, valFont, textBrush, barX + barW + 4, rowY + 2);

                        // Formatted CPU time
                        g.DrawString(item.FormattedCpuTime, valFont, dimBrush, 252, rowY + 2);

                        rowY += 28;
                    }
                }
            }

            // Footer
            int footerY = h - 22;
            using (SolidBrush footerBg = new SolidBrush(Color.FromArgb(17, 17, 27)))
            {
                g.FillRectangle(footerBg, 1, footerY, w - 2, 21);
            }
            using (Pen sepPen = new Pen(Color.FromArgb(49, 50, 68), 1))
            {
                g.DrawLine(sepPen, 0, footerY, w, footerY);
            }

            using (Font footFont = new Font("Segoe UI", 7F, FontStyle.Regular))
            using (SolidBrush footBrush = new SolidBrush(Color.FromArgb(130, 135, 160)))
            {
                string footText = string.Format("Опрос: {0}с  •  Период: {1} мин  •  Топ: {2}", _settings.TopProcessesPollIntervalSec, _settings.TopProcessesWindowMinutes, _settings.TopProcessesCount);
                g.DrawString(footText, footFont, footBrush, 12, footerY + 4);
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

            using (Font btnFont = new Font("Segoe UI", 9F, FontStyle.Regular))
            using (SolidBrush textBrush = new SolidBrush(foreColor))
            {
                SizeF size = g.MeasureString(text, btnFont);
                float x = rect.X + (rect.Width - size.Width) / 2f;
                float y = rect.Y + (rect.Height - size.Height) / 2f;
                g.DrawString(text, btnFont, textBrush, x, y);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_uiTimer != null)
                {
                    _uiTimer.Stop();
                    _uiTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
