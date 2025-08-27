using MultiAxisChartApp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace test2508
{
    public partial class Form1 : Form
    {
        private MultiAxisChart chart;
        private Panel rightPanel;
        private CheckBox[] chkEnable = new CheckBox[5];
        private LinkLabel[] linkAuto = new LinkLabel[5];

        public Form1()
        {
            InitializeComponent();
            Width = 1280; Height = 760;

            chart = new MultiAxisChart() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f) };
            Controls.Add(chart);

            rightPanel = new Panel() { Dock = DockStyle.Right, Width = 300, Padding = new Padding(8) };
            Controls.Add(rightPanel);

            var flow = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            rightPanel.Controls.Add(flow);

            for (int i = 0; i < 5; i++)
            {
                var panel = new Panel() { Width = rightPanel.Width - 24, Height = 56, BorderStyle = BorderStyle.None };
                var cb = new CheckBox() { Text = $"Series {i + 1}", Checked = false, Left = 6, Top = 6, Width = 140 };
                panel.Controls.Add(cb);

                var lnk = new LinkLabel() { Text = "Auto min/max", Left = 150, Top = 8, Width = 120 };
                panel.Controls.Add(lnk);

                chkEnable[i] = cb;
                linkAuto[i] = lnk;
                int idx = i;

                cb.CheckedChanged += (s, e) =>
                {
                    chart.SeriesEnabled[idx] = cb.Checked;
                    chart.Invalidate();
                };

                lnk.Click += (s, e) =>
                {
                    var arr = chart.Series[idx];
                    if (arr != null && arr.Length > 0)
                    {
                        double min = arr.Min(), max = arr.Max();
                        double pad = (max - min) * 0.05;
                        if (Math.Abs(max - min) < 1e-9) { max = min + 1; pad = 0.1; }
                        chart.SetAxisRange(idx, min - pad, max + pad);
                    }
                };

                flow.Controls.Add(panel);
            }

            var info = new Label()
            {
                AutoSize = false,
                Height = 160,
                Dock = DockStyle.Bottom
            };
            rightPanel.Controls.Add(info);

            // Example: pick portion of the form the chart occupies (normalized)
           //chart.ChartAreaNormalized = new RectangleF(0.10f, 0.10f, 0.87f, 0.94f);

            GenerateTestData();
        }

        private void GenerateTestData()
        {
            int n = 1000;
            double[] s0 = Enumerable.Range(0, n).Select(i => Math.Sin(i * 0.02)).ToArray();
            double[] s1 = Enumerable.Range(0, n).Select(i => Math.Cos(i * 0.015) * 10.0).ToArray();
            var rnd = new Random(123);
            double[] s2 = Enumerable.Range(0, n).Select(i => i * 0.01 + (rnd.NextDouble() - 0.5) * 0.5).ToArray();
            double[] s3 = Enumerable.Range(0, n).Select(i => Math.Exp(-i * 0.001) * Math.Sin(i * 0.05) * 5.0).ToArray();
            double val = 0;
            double[] s4 = new double[n];
            for (int i = 0; i < n; i++) { val += (rnd.NextDouble() - 0.5) * 0.2; s4[i] = val; }

            chart.SetSeries(0, s0, true, "S1");
            chart.SetSeries(1, s1, true, "S2");
            chart.SetSeries(2, s2, true, "S3");
            chart.SetSeries(3, s3, true, "S4");
            chart.SetSeries(4, s4, true, "S5");

            // enable the checkboxes
            for (int i = 0; i < 5; i++) chkEnable[i].Checked = true;

            // reset cursors
            for (int c = 0; c < chart.ChartCursors.Length; c++)
            {
                chart.ChartCursors[c].Enabled = false;
                chart.ChartCursors[c].Index = 0;
            }

            chart.AutoScaleAll();
            chart.Invalidate();
        }
    }

}
