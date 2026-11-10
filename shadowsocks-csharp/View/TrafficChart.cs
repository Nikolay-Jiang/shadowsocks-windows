using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Shadowsocks.View
{
    /// <summary>
    /// Small traffic graph used by the log viewer.  This replaces the old
    /// System.Windows.Forms.DataVisualization.Charting.Chart, which is not
    /// available on .NET (Core) / .NET 10.
    /// </summary>
    public sealed class TrafficChart : Control
    {
        private static readonly Color InboundColor = Color.FromArgb(255, 128, 0);
        private static readonly Color OutboundColor = Color.FromArgb(88, 118, 255);
        private static readonly Color GridColor = Color.FromArgb(224, 224, 224);
        private static readonly Color AxisColor = Color.FromArgb(140, 140, 140);

        private float[] _inbound = Array.Empty<float>();
        private float[] _outbound = Array.Empty<float>();
        private float _maximum = 50;
        private string _unitName = "";
        private string _inboundText = "";
        private string _outboundText = "";
        private string _inboundLabel = "Inbound";
        private string _outboundLabel = "Outbound";

        public TrafficChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 8F);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string InboundLabel
        {
            get => _inboundLabel;
            set { _inboundLabel = value ?? ""; Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string OutboundLabel
        {
            get => _outboundLabel;
            set { _outboundLabel = value ?? ""; Invalidate(); }
        }

        public void UpdateData(IReadOnlyList<float> inbound, IReadOnlyList<float> outbound,
            float maximum, string unitName, string inboundText, string outboundText)
        {
            _inbound = Copy(inbound);
            _outbound = Copy(outbound);
            _maximum = maximum <= 0 ? 50 : maximum;
            _unitName = unitName ?? "";
            _inboundText = inboundText ?? "";
            _outboundText = outboundText ?? "";
            Invalidate();
        }

        private static float[] Copy(IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<float>();
            }
            var result = new float[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }
            return result;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            const int left = 46;
            const int right = 12;
            const int top = 26;
            const int bottom = 10;
            int width = Width - left - right;
            int height = Height - top - bottom;
            if (width <= 4 || height <= 4)
            {
                return;
            }

            using (var gridPen = new Pen(GridColor))
            using (var axisPen = new Pen(AxisColor))
            using (var textBrush = new SolidBrush(Color.FromArgb(90, 90, 90)))
            {
                for (int i = 0; i <= 4; i++)
                {
                    float y = top + height - height * i / 4f;
                    g.DrawLine(gridPen, left, y, left + width, y);
                    float value = _maximum * i / 4f;
                    string label = value.ToString("0.##") + (i == 4 && _unitName.Length > 0 ? " " + _unitName : "");
                    SizeF size = g.MeasureString(label, Font);
                    g.DrawString(label, Font, textBrush, left - size.Width - 6, y - size.Height / 2);
                }

                for (int i = 0; i <= 12; i++)
                {
                    float x = left + width * i / 12f;
                    g.DrawLine(gridPen, x, top, x, top + height);
                }

                g.DrawLine(axisPen, left, top, left, top + height);
                g.DrawLine(axisPen, left, top + height, left + width, top + height);

                DrawSeries(g, _inbound, InboundColor, left, top, width, height);
                DrawSeries(g, _outbound, OutboundColor, left, top, width, height);
            }

            DrawLegend(g, left, top, width);
        }

        private void DrawSeries(Graphics g, float[] values, Color color, int left, int top, int width, int height)
        {
            if (values.Length < 2)
            {
                return;
            }
            using (var pen = new Pen(color, 2f))
            {
                pen.LineJoin = LineJoin.Round;
                var points = new PointF[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    float x = left + width * i / Math.Max(1, values.Length - 1f);
                    float v = Math.Max(0, Math.Min(_maximum, values[i]));
                    float y = top + height - height * (v / _maximum);
                    points[i] = new PointF(x, y);
                }
                g.DrawLines(pen, points);
            }
        }

        private void DrawLegend(Graphics g, int left, int top, int width)
        {
            float x = left;
            const float y = 7;
            const int swatch = 14;
            using (var textBrush = new SolidBrush(Color.FromArgb(70, 70, 70)))
            {
                using (var inboundPen = new Pen(InboundColor, 3f))
                using (var outboundPen = new Pen(OutboundColor, 3f))
                {
                    g.DrawLine(inboundPen, x, y + 7, x + swatch, y + 7);
                    x += swatch + 5;
                    string inbound = $"{_inboundLabel} {_inboundText}";
                    g.DrawString(inbound, Font, textBrush, x, y);
                    x += g.MeasureString(inbound, Font).Width + 18;

                    g.DrawLine(outboundPen, x, y + 7, x + swatch, y + 7);
                    x += swatch + 5;
                    string outbound = $"{_outboundLabel} {_outboundText}";
                    g.DrawString(outbound, Font, textBrush, x, y);
                }
            }
        }
    }
}
