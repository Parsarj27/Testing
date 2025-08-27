using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MultiAxisChartApp
{
    // Single-file sample: MultiAxisChart control + Form1 + Program.Main

    public class MultiAxisChart : Control
    {
        // Data containers (5 series max)
        public List<double[]> Series { get; } = new List<double[]>();
        public List<string> SeriesNames { get; } = new List<string>();
        public List<bool> SeriesEnabled { get; } = new List<bool>();
        public List<(double Min, double Max)> AxisRanges { get; } = new List<(double, double)>();

        // Visual colors
        private readonly Color[] SeriesColors = { Color.DodgerBlue, Color.Red, Color.Green, Color.Orange, Color.Purple };

        // view transform (X axis)
        private double panX = 0.0;
        private double zoomX = 1.0;

        // interaction
        private bool isPanning = false;
        private Point panStartMouse;

        // rectangle zoom
        private bool isRectZoom = false;
        private Point rectStart;
        private Rectangle rectCurrent;

        // mouse/cursor
        private Point mousePos;
        private bool mouseInside = false;

        // Cursors: 3 available
        public class CursorInfo
        {
            public bool Enabled;
            public int Index = 0; // data index
            public Color Color;
            public string Name;
        }
        public CursorInfo[] ChartCursors = new CursorInfo[3];

        // hit-test rectangles for axis min/max labels (5 axes)
        private Rectangle[] axisMinRects = new Rectangle[5];
        private Rectangle[] axisMaxRects = new Rectangle[5];

        // Normalized area the chart occupies inside the control (fractions 0..1)
        public RectangleF ChartAreaNormalized { get; set; } = new RectangleF(0.06f, 0.04f, 0.92f, 0.92f);

        // spacing used for left-side per-axis clickable boxes
        private int leftAxisSpacing = 36; // reduced spacing so axes are closer
        private int axisBaseOffset = 8;   // how close the first axis is to plot left

        public MultiAxisChart()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);

            // init containers for 5 series
            for (int i = 0; i < 5; i++)
            {
                Series.Add(new double[0]);
                SeriesNames.Add($"Series {i + 1}");
                SeriesEnabled.Add(false);
                AxisRanges.Add((0.0, 1.0));
            }

            // init cursors
            ChartCursors[0] = new CursorInfo() { Enabled = false, Color = Color.Magenta, Name = "Cursor 1" };
            ChartCursors[1] = new CursorInfo() { Enabled = false, Color = Color.Teal, Name = "Cursor 2" };
            ChartCursors[2] = new CursorInfo() { Enabled = false, Color = Color.Goldenrod, Name = "Cursor 3" };

            // mouse events
            MouseWheel += MultiAxisChart_MouseWheel;
            MouseDown += MultiAxisChart_MouseDown;
            MouseMove += MultiAxisChart_MouseMove;
            MouseUp += MultiAxisChart_MouseUp;
            MouseEnter += (s, e) => { mouseInside = true; Invalidate(); };
            MouseLeave += (s, e) => { mouseInside = false; Invalidate(); };
        }

        // External helpers
        public void SetSeries(int index, double[] values, bool enabled = true, string name = null)
        {
            if (index < 0 || index >= 5) throw new ArgumentOutOfRangeException(nameof(index));
            Series[index] = values ?? new double[0];
            SeriesEnabled[index] = enabled;
            if (!string.IsNullOrEmpty(name)) SeriesNames[index] = name;
            if (AxisRanges[index].Min == AxisRanges[index].Max)
            {
                var min = values.Length > 0 ? values.Min() : 0;
                var max = values.Length > 0 ? values.Max() : min + 1;
                AxisRanges[index] = (min, max);
            }
            Invalidate();
        }

        public void SetAxisRange(int index, double min, double max)
        {
            if (index < 0 || index >= 5) return;
            if (min == max) max = min + 1e-9;
            AxisRanges[index] = (min, max);
            Invalidate();
        }

        // Autoscale all enabled series (used for Reset View)
        public void AutoScaleAll()
        {
            int n = GetMaxPoints();
            if (n <= 1)
            {
                for (int a = 0; a < 5; a++) AxisRanges[a] = (0, 1);
            }
            else
            {
                for (int a = 0; a < 5; a++)
                {
                    if (!SeriesEnabled[a] || Series[a] == null || Series[a].Length == 0) continue;
                    double min = Series[a].Min();
                    double max = Series[a].Max();
                    double pad = (max - min) * 0.05;
                    if (Math.Abs(max - min) < 1e-9) { max = min + 1; pad = 0.1; }
                    AxisRanges[a] = (min - pad, max + pad);
                }
            }

            // Reset X view to show full data
            panX = 0.0;
            zoomX = 1.0;
            Invalidate();
        }

        // Layout for plotting area (uses ChartAreaNormalized)
        private Rectangle PlotArea
        {
            get
            {
                int left = (int)(ChartAreaNormalized.X * ClientSize.Width);
                int top = (int)(ChartAreaNormalized.Y * ClientSize.Height);
                int w = Math.Max(10, (int)(ChartAreaNormalized.Width * ClientSize.Width));
                int h = Math.Max(10, (int)(ChartAreaNormalized.Height * ClientSize.Height));
                return new Rectangle(left, top, w, h);
            }
        }

        // Mouse handlers
        private void MultiAxisChart_MouseWheel(object sender, MouseEventArgs e)
        {
            const double zoomFactor = 1.12;
            double oldZoom = zoomX;
            if (e.Delta > 0) zoomX *= 1.0 / zoomFactor; else zoomX *= zoomFactor;
            zoomX = Math.Max(0.05, Math.Min(100.0, zoomX));

            Rectangle plot = PlotArea;
            if (plot.Width <= 0) return;
            double mouseFrac = (double)(e.X - plot.Left) / plot.Width;
            double worldXBefore = (mouseFrac - panX) / oldZoom;
            panX = mouseFrac - worldXBefore * zoomX;
            ClampPan();
            Invalidate();
        }

        private void MultiAxisChart_MouseDown(object sender, MouseEventArgs e)
        {
            // Check left-click on axis min/max labels first
            if (e.Button == MouseButtons.Left)
            {
                for (int a = 0; a < 5; a++)
                {
                    if (!SeriesEnabled[a]) continue;
                    if (axisMinRects[a] != Rectangle.Empty && axisMinRects[a].Contains(e.Location))
                    {
                        double current = AxisRanges[a].Min;
                        double? result = ShowInputDouble($"Edit Min for {SeriesNames[a]}", current);
                        if (result.HasValue)
                        {
                            double newMin = result.Value;
                            var (_, oldMax) = AxisRanges[a];
                            if (newMin >= oldMax) oldMax = newMin + 1e-6;
                            AxisRanges[a] = (newMin, oldMax);
                            Invalidate();
                        }
                        return;
                    }
                    if (axisMaxRects[a] != Rectangle.Empty && axisMaxRects[a].Contains(e.Location))
                    {
                        double current = AxisRanges[a].Max;
                        double? result = ShowInputDouble($"Edit Max for {SeriesNames[a]}", current);
                        if (result.HasValue)
                        {
                            double newMax = result.Value;
                            var (oldMin, _) = AxisRanges[a];
                            if (newMax <= oldMin) oldMin = newMax - 1e-6;
                            AxisRanges[a] = (oldMin, newMax);
                            Invalidate();
                        }
                        return;
                    }
                }
            }

            if (e.Button == MouseButtons.Left)
            {
                // pan start
                isPanning = true;
                panStartMouse = e.Location;
                Cursor = Cursors.Hand;
            }
            else if (e.Button == MouseButtons.Right)
            {
                // open context menu
                ShowContextMenu(e.Location);
            }
        }

        private void MultiAxisChart_MouseMove(object sender, MouseEventArgs e)
        {
            mousePos = e.Location;
            Rectangle plot = PlotArea;
            if (isPanning && e.Button == MouseButtons.Left)
            {
                if (plot.Width > 0 && plot.Height > 0)
                {
                    double dx = (e.X - panStartMouse.X) / (double)plot.Width;
                    panX += dx;

                    // vertical pan: shift each enabled axis ranges by fraction of its span
                    double dyFrac = (e.Y - panStartMouse.Y) / (double)plot.Height; // positive when mouse moved down
                    for (int a = 0; a < 5; a++)
                    {
                        if (!SeriesEnabled[a]) continue;
                        var (min, max) = AxisRanges[a];
                        double span = max - min;
                        double delta = dyFrac * span; // move ranges by fraction-of-span
                        AxisRanges[a] = (min + delta, max + delta);
                    }

                    panStartMouse = e.Location;
                    ClampPan();
                    Invalidate();
                }
            }
            else if (isRectZoom && e.Button == MouseButtons.Right)
            {
                rectCurrent = GetNormalizedRect(rectStart, e.Location);
                Invalidate();
            }
            else
            {
                // hover
                Invalidate();
            }
        }

        private void MultiAxisChart_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isPanning)
            {
                isPanning = false; Cursor = Cursors.Default;
            }
            else if (e.Button == MouseButtons.Right && isRectZoom)
            {
                isRectZoom = false;
                if (rectCurrent.Width > 6 && rectCurrent.Height > 6)
                {
                    ApplyRectZoom(rectCurrent);
                }
                rectCurrent = Rectangle.Empty;
                Invalidate();
            }
        }

        private void ShowContextMenu(Point clientPt)
        {
            var m = new ContextMenuStrip();
            // Cursor actions
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var miToggle = new ToolStripMenuItem((ChartCursors[idx].Enabled ? "Disable " : "Enable ") + ChartCursors[idx].Name);
                miToggle.Click += (s, e) =>
                {
                    ChartCursors[idx].Enabled = !ChartCursors[idx].Enabled;
                    if (ChartCursors[idx].Enabled)
                    {
                        int nn = GetMaxPoints();
                        if (nn > 1 && PlotArea.Contains(clientPt))
                        {
                            int nearest = GetNearestIndexFromPixelX(clientPt.X);
                            ChartCursors[idx].Index = nearest;
                        }
                    }
                    Invalidate();
                };
                m.Items.Add(miToggle);

                var miSet = new ToolStripMenuItem($"Set {ChartCursors[idx].Name} Here");
                miSet.Click += (s, e) =>
                {
                    if (PlotArea.Contains(clientPt))
                    {
                        int nearest = GetNearestIndexFromPixelX(clientPt.X);
                        ChartCursors[idx].Index = nearest;
                        ChartCursors[idx].Enabled = true;
                        Invalidate();
                    }
                };
                m.Items.Add(miSet);

                m.Items.Add(new ToolStripSeparator());
            }

            // rectangle zoom toggle & reset view
            var miRect = new ToolStripMenuItem("Start Rectangle Zoom (right-drag)");
            miRect.Click += (s, e) =>
            {
                isRectZoom = true;
                rectStart = clientPt;
                rectCurrent = new Rectangle(rectStart, Size.Empty);
            };
            m.Items.Add(miRect);

            var miReset = new ToolStripMenuItem("Reset View (autoscale)");
            miReset.Click += (s, e) =>
            {
                AutoScaleAll();
            };
            m.Items.Add(miReset);

            m.Show(this, clientPt);
        }

        private int GetMaxPoints()
        {
            return Series.Select(s => s?.Length ?? 0).DefaultIfEmpty(0).Max();
        }

        private int GetNearestIndexFromPixelX(int pixelX)
        {
            int n = GetMaxPoints();
            if (n <= 1) return 0;
            Rectangle plot = PlotArea;
            double frac = (pixelX - plot.Left) / (double)plot.Width;
            double worldX = panX + frac / zoomX;
            double idxf = worldX * (n - 1);
            int nearest = (int)Math.Round(idxf);
            nearest = Math.Max(0, Math.Min(n - 1, nearest));
            return nearest;
        }

        // rectangle zoom implementation
        private void ApplyRectZoom(Rectangle rect)
        {
            Rectangle plot = PlotArea;
            if (plot.Width <= 0 || plot.Height <= 0) return;

            double x1frac = (rect.Left - plot.Left) / (double)plot.Width;
            double x2frac = (rect.Right - plot.Left) / (double)plot.Width;
            x1frac = Clamp01(x1frac); x2frac = Clamp01(x2frac);
            if (x2frac > x1frac + 1e-6)
            {
                double newZoom = 1.0 / (x2frac - x1frac);
                double newPan = x1frac;
                zoomX = newZoom;
                panX = newPan;
                ClampPan();
            }

            // adjust each enabled axis vertically to the rect's top/bottom mapping
            for (int a = 0; a < 5; a++)
            {
                if (!SeriesEnabled[a]) continue;
                var (min, max) = AxisRanges[a];
                double topFrac = 1.0 - (double)(rect.Top - plot.Top) / plot.Height;
                double bottomFrac = 1.0 - (double)(rect.Bottom - plot.Top) / plot.Height;
                topFrac = Clamp01(topFrac); bottomFrac = Clamp01(bottomFrac);
                double valTop = min + topFrac * (max - min);
                double valBottom = min + bottomFrac * (max - min);
                double nmin = Math.Min(valTop, valBottom);
                double nmax = Math.Max(valTop, valBottom);
                if (Math.Abs(nmax - nmin) < 1e-12) nmax = nmin + 1e-6;
                AxisRanges[a] = (nmin, nmax);
            }
        }

        private Rectangle GetNormalizedRect(Point a, Point b)
        {
            int x1 = Math.Min(a.X, b.X), x2 = Math.Max(a.X, b.X);
            int y1 = Math.Min(a.Y, b.Y), y2 = Math.Max(a.Y, b.Y);
            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        private double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));
        private void ClampPan()
        {
            double viewWidthFrac = 1.0 / zoomX;
            if (viewWidthFrac >= 1.0)
            {
                panX = 0; zoomX = 1.0; return;
            }
            panX = Math.Max(-0.5, Math.Min(1.0 - viewWidthFrac + 0.5, panX));
        }

        // Paint
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            Rectangle plot = PlotArea;
            if (plot.Width <= 0 || plot.Height <= 0) return;

            // plot border
            g.DrawRectangle(Pens.Black, plot);

            // reset hit rectangles
            for (int i = 0; i < 5; i++) { axisMinRects[i] = Rectangle.Empty; axisMaxRects[i] = Rectangle.Empty; }

            int nPoints = GetMaxPoints();

            // draw X axis ticks/gridlines (24) BEFORE drawing data so gridlines are behind series
            DrawXAxis(g, plot, nPoints);

            // draw horizontal grid lines for each enabled axis (below the data)
            for (int a = 0; a < 5; a++)
            {
                if (!SeriesEnabled[a]) continue; // only draw grids for enabled axes
                var (min, max) = AxisRanges[a];
                for (int i = 0; i <= 18; i++)
                {
                    double frac = i / 18.0;
                    int y = plot.Bottom - (int)(frac * plot.Height);
                    using (var pen = new Pen(Color.FromArgb(220, 220, 220)))
                        g.DrawLine(pen, plot.Left, y, plot.Right, y);

                    // connector tick to left to visually connect the gridline to the left-side boxes
                    g.DrawLine(Pens.Gray, plot.Left - 6, y, plot.Left, y);
                }
            }

            // draw series lines (clip to plot area for neatness)
            if (nPoints > 1)
            {
                Region oldClip = g.Clip;
                g.SetClip(plot);

                for (int s = 0; s < 5; s++)
                {
                    if (!SeriesEnabled[s]) continue;
                    var data = Series[s];
                    if (data == null || data.Length < 2) continue;
                    var (ymin, ymax) = AxisRanges[s];
                    using (var pen = new Pen(SeriesColors[s % SeriesColors.Length], 2f))
                    {
                        PointF? prev = null;
                        for (int i = 0; i < data.Length; i++)
                        {
                            double xFrac = (i / (double)(nPoints - 1) - panX) * zoomX;
                            if (xFrac < -0.05 || xFrac > 1.05) { prev = null; continue; }
                            double yFrac = (data[i] - ymin) / (ymax - ymin);
                            float px = plot.Left + (float)(xFrac * plot.Width);
                            float py = plot.Bottom - (float)(yFrac * plot.Height);
                            var cur = new PointF(px, py);
                            if (prev != null) g.DrawLine(pen, prev.Value, cur);
                            prev = cur;
                        }
                    }
                }

                g.Clip = oldClip;
            }

            // draw min/max clickable boxes (aligned with top/bottom gridlines) for each enabled axis
            for (int a = 0; a < 5; a++)
            {
                if (!SeriesEnabled[a]) { axisMaxRects[a] = Rectangle.Empty; axisMinRects[a] = Rectangle.Empty; continue; }

                var (min, max) = AxisRanges[a];
                string maxStr = max.ToString("0.###");
                var maxSize = g.MeasureString(maxStr, Font);
                int maxRectW = (int)maxSize.Width + 6;
                int maxRectH = (int)maxSize.Height + 4;
                int topGridY = plot.Top; // top gridline y
                int axisX = plot.Left - axisBaseOffset - (a * leftAxisSpacing); // put columns left of plot, closer spacing
                var maxRect = new Rectangle(axisX - maxRectW, topGridY - maxRectH / 2, maxRectW, maxRectH);
                using (var b = new SolidBrush(Color.FromArgb(220, 240, 255)))
                    g.FillRectangle(b, maxRect);
                g.DrawRectangle(Pens.Gray, maxRect);
                g.DrawString(maxStr, Font, Brushes.Black, maxRect.Left + 3, maxRect.Top + 2);
                axisMaxRects[a] = maxRect;

                // Bottom (Min) clickable label
                string minStr = min.ToString("0.###");
                var minSize = g.MeasureString(minStr, Font);
                int minRectW = (int)minSize.Width + 6;
                int minRectH = (int)minSize.Height + 4;
                int bottomGridY = plot.Bottom;
                var minRect = new Rectangle(axisX - minRectW, bottomGridY - minRectH / 2, minRectW, minRectH);
                using (var b2 = new SolidBrush(Color.FromArgb(220, 240, 255)))
                    g.FillRectangle(b2, minRect);
                g.DrawRectangle(Pens.Gray, minRect);
                g.DrawString(minStr, Font, Brushes.Black, minRect.Left + 3, minRect.Top + 2);
                axisMinRects[a] = minRect;
            }

            // legend top-left inside plot (semi-transparent)
            DrawLegend(g, plot);

            // draw rectangle zoom preview
            if (isRectZoom && !rectCurrent.IsEmpty)
            {
                using (var pen = new Pen(Color.Black) { DashStyle = DashStyle.Dash })
                using (var brush = new SolidBrush(Color.FromArgb(40, Color.LightGray)))
                {
                    g.FillRectangle(brush, rectCurrent);
                    g.DrawRectangle(pen, rectCurrent);
                }
            }

            // draw cursors (vertical lines and value boxes) only if visible inside current view
            for (int ci = 0; ci < ChartCursors.Length; ci++)
            {
                var cur = ChartCursors[ci];
                if (!cur.Enabled) continue;
                if (nPoints <= 1) continue;
                int idx = Math.Max(0, Math.Min(nPoints - 1, cur.Index));
                double idxXFrac = (idx / (double)(nPoints - 1) - panX) * zoomX;
                if (idxXFrac < 0.0 || idxXFrac > 1.0) continue; // do not draw cursor outside visible zoom

                int px = plot.Left + (int)(idxXFrac * plot.Width);
                using (var pen = new Pen(cur.Color, 2f))
                {
                    g.DrawLine(pen, px, plot.Top, px, plot.Bottom);
                }

                // small info box at top showing index & values
                var lines = new List<string> { $"{cur.Name}: idx={idx}" };
                for (int s = 0; s < 5; s++)
                {
                    if (!SeriesEnabled[s]) continue;
                    var arr = Series[s];
                    if (arr == null || arr.Length <= idx) continue;
                    lines.Add($"{SeriesNames[s]} = {arr[idx]:0.#####}");
                }

                int lineH = (int)g.MeasureString("A", Font).Height;
                int boxW = 220;
                int boxH = lineH * lines.Count + 8;
                int boxX = Math.Min(Width - boxW - 8, Math.Max(plot.Left + 4, px + 6));
                int boxY = plot.Top + 6;
                using (var bb = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                {
                    g.FillRectangle(bb, boxX, boxY, boxW, boxH);
                    g.DrawRectangle(Pens.Black, boxX, boxY, boxW, boxH);
                }
                for (int i = 0; i < lines.Count; i++)
                    g.DrawString(lines[i], Font, new SolidBrush(cur.Color), boxX + 6, boxY + 4 + i * lineH);

                // draw small x-value under the axis in cursor color
                string xlbl = idx.ToString();
                var xsz = g.MeasureString(xlbl, Font);
                int xLblX = px - (int)(xsz.Width / 2f);
                int xLblY = plot.Bottom + 8;
                using (var brush = new SolidBrush(cur.Color))
                {
                    g.DrawString(xlbl, Font, brush, xLblX, xLblY);
                }
            }

            // if mouse inside plot, draw a thin dashed line at nearest index for hover (not active cursor)
            if (mouseInside && plot.Contains(mousePos) && nPoints > 1)
            {
                int nearest = GetNearestIndexFromPixelX(mousePos.X);
                double frac = (nearest / (double)(nPoints - 1) - panX) * zoomX;
                int px = plot.Left + (int)(frac * plot.Width);
                using (var pen = new Pen(Color.Gray) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, px, plot.Top, px, plot.Bottom);
            }
        }

        private void DrawLegend(Graphics g, Rectangle plot)
        {
            var items = new List<(Color color, string name)>();
            for (int i = 0; i < 5; i++)
            {
                if (!SeriesEnabled[i]) continue;
                items.Add((SeriesColors[i % SeriesColors.Length], string.IsNullOrEmpty(SeriesNames[i]) ? $"Series {i + 1}" : SeriesNames[i]));
            }

            if (items.Count == 0) return;

            int padding = 6;
            int itemH = (int)g.MeasureString("A", Font).Height + 4;
            int boxW = 140;
            int boxH = padding * 2 + items.Count * itemH;
            int bx = plot.Left + 8;
            int by = plot.Top + 8;

            using (var b = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                g.FillRectangle(b, bx, by, boxW, boxH);
            g.DrawRectangle(Pens.Gray, bx, by, boxW, boxH);

            for (int i = 0; i < items.Count; i++)
            {
                int iy = by + padding + i * itemH;
                var clr = items[i].color;
                g.FillRectangle(new SolidBrush(clr), bx + 6, iy + 3, 14, 10);
                g.DrawRectangle(Pens.Black, bx + 6, iy + 3, 14, 10);
                g.DrawString(items[i].name, Font, Brushes.Black, bx + 26, iy);
            }
        }

        private void DrawXAxis(Graphics g, Rectangle plot, int nPoints)
        {
            // draw baseline
            g.DrawLine(Pens.Black, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

            if (nPoints <= 1) return;

            // compute visible index range
            double worldLeft = panX; // corresponds to fraction 0
            double worldRight = panX + 1.0 / zoomX; // corresponds to fraction 1
            double idxLeft = worldLeft * (nPoints - 1);
            double idxRight = worldRight * (nPoints - 1);

            int tickCount = 24;
            var penGrid = new Pen(Color.FromArgb(220, 220, 220));
            for (int t = 0; t < tickCount; t++)
            {
                double idx = idxLeft + (idxRight - idxLeft) * t / (tickCount - 1);
                double worldFrac = idx / (nPoints - 1);
                double xFrac = (worldFrac - panX) * zoomX;
                if (xFrac < -0.05 || xFrac > 1.05) continue;
                int px = plot.Left + (int)(xFrac * plot.Width);

                // vertical gridline across plot
                g.DrawLine(penGrid, px, plot.Top, px, plot.Bottom);

                // tick mark and label at bottom
                g.DrawLine(Pens.Black, px, plot.Bottom, px, plot.Bottom + 6);
                string lbl = ((int)Math.Round(idx)).ToString();
                var sz = g.MeasureString(lbl, Font);
                g.DrawString(lbl, Font, Brushes.Black, px - sz.Width / 2f, plot.Bottom + 6);
            }
        }

        // Helper: show a small input dialog and return double? (null => cancel)
        private double? ShowInputDouble(string title, double current)
        {
            using (var frm = new Form() { Width = 340, Height = 120, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, Text = title, MinimizeBox = false, MaximizeBox = false })
            {
                var lbl = new Label() { Left = 10, Top = 10, Text = title, AutoSize = true };
                frm.Controls.Add(lbl);
                var tb = new TextBox() { Left = 10, Top = 34, Width = 300, Text = current.ToString("G") };
                frm.Controls.Add(tb);
                var ok = new Button() { Text = "OK", DialogResult = DialogResult.OK, Left = 150, Width = 80, Top = 64 };
                var cancel = new Button() { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 240, Width = 80, Top = 64 };
                frm.Controls.Add(ok); frm.Controls.Add(cancel);
                frm.AcceptButton = ok; frm.CancelButton = cancel;

                if (frm.ShowDialog(this) == DialogResult.OK)
                {
                    if (double.TryParse(tb.Text, out double val))
                        return val;
                    else
                    {
                        MessageBox.Show(this, "Invalid number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return null;
                    }
                }
                return null;
            }
        }
    }

}
