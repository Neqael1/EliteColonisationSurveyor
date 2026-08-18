using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using EliteColonisationSurveyor.Core;

namespace EliteColonisationSurveyor.Plugin
{
    internal enum RouteProjection { TopXZ, FrontXY, SideZY }

    internal sealed class RouteMapControl : Control
    {
        private readonly ToolTip toolTip = new ToolTip();
        private IReadOnlyList<StarSystem> route = new List<StarSystem>();
        private readonly List<Tuple<RectangleF, StarSystem, int>> hitTargets = new List<Tuple<RectangleF, StarSystem, int>>();
        private RouteProjection projection;
        private string lastTip;

        public RouteMapControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(10, 16, 25);
            ForeColor = Color.Gainsboro;
            Resize += (_, __) => Invalidate();
            MouseMove += ShowPointDetails;
            MouseLeave += (_, __) => { toolTip.Hide(this); lastTip = null; };
        }

        public RouteProjection Projection
        {
            get => projection;
            set { projection = value; Invalidate(); }
        }

        public void SetRoute(IReadOnlyList<StarSystem> value)
        {
            route = value ?? new List<StarSystem>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            hitTargets.Clear();

            var plot = new RectangleF(58, 24, Math.Max(10, ClientSize.Width - 82), Math.Max(10, ClientSize.Height - 72));
            using (var frame = new Pen(Color.FromArgb(75, 128, 160))) e.Graphics.DrawRectangle(frame, plot.X, plot.Y, plot.Width, plot.Height);
            if (route.Count == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(170, ForeColor)))
                    e.Graphics.DrawString("Generate a route to display its map.", Font, brush, plot.X + 16, plot.Y + 16);
                return;
            }

            var points = route.Select(Project).ToList();
            var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
            PadDomain(ref minX, ref maxX); PadDomain(ref minY, ref maxY);
            Func<PointF, PointF> screen = p => new PointF(
                plot.Left + (float)((p.X - minX) / (maxX - minX) * plot.Width),
                plot.Bottom - (float)((p.Y - minY) / (maxY - minY) * plot.Height));

            DrawGrid(e.Graphics, plot, minX, maxX, minY, maxY);
            var screenPoints = points.Select(screen).ToArray();
            using (var pathPen = new Pen(Color.FromArgb(65, 190, 255), 2f))
            {
                pathPen.CustomEndCap = new AdjustableArrowCap(3, 5);
                for (var i = 1; i < screenPoints.Length; i++) e.Graphics.DrawLine(pathPen, screenPoints[i - 1], screenPoints[i]);
            }

            for (var i = 0; i < screenPoints.Length; i++)
            {
                var radius = i == 0 ? 7f : 5f;
                var rect = new RectangleF(screenPoints[i].X - radius, screenPoints[i].Y - radius, radius * 2, radius * 2);
                using (var brush = new SolidBrush(i == 0 ? Color.Gold : ScoreColour(route[i].CandidateScore))) e.Graphics.FillEllipse(brush, rect);
                using (var outline = new Pen(Color.White, 1f)) e.Graphics.DrawEllipse(outline, rect);
                hitTargets.Add(Tuple.Create(RectangleF.Inflate(rect, 8, 8), route[i], i));
                if (i == 0 || i == screenPoints.Length - 1)
                    using (var brush = new SolidBrush(ForeColor)) e.Graphics.DrawString(i == 0 ? "Centre" : i.ToString(), Font, brush, screenPoints[i].X + 7, screenPoints[i].Y - 15);
            }

            DrawAxisLabels(e.Graphics, plot, minX, maxX, minY, maxY);
        }

        private void DrawGrid(Graphics graphics, RectangleF plot, double minX, double maxX, double minY, double maxY)
        {
            using (var pen = new Pen(Color.FromArgb(30, 128, 160)))
            using (var brush = new SolidBrush(Color.FromArgb(165, ForeColor)))
            {
                for (var i = 0; i <= 4; i++)
                {
                    var x = plot.Left + plot.Width * i / 4f;
                    var y = plot.Bottom - plot.Height * i / 4f;
                    graphics.DrawLine(pen, x, plot.Top, x, plot.Bottom);
                    graphics.DrawLine(pen, plot.Left, y, plot.Right, y);
                    graphics.DrawString((minX + (maxX - minX) * i / 4).ToString("0.#"), Font, brush, x - 12, plot.Bottom + 5);
                    graphics.DrawString((minY + (maxY - minY) * i / 4).ToString("0.#"), Font, brush, 4, y - 7);
                }
            }
        }

        private void DrawAxisLabels(Graphics graphics, RectangleF plot, double minX, double maxX, double minY, double maxY)
        {
            var labels = projection == RouteProjection.TopXZ ? new[] { "Galactic X (ly)", "Galactic Z (ly)" }
                       : projection == RouteProjection.FrontXY ? new[] { "Galactic X (ly)", "Galactic Y (ly)" }
                       : new[] { "Galactic Z (ly)", "Galactic Y (ly)" };
            using (var brush = new SolidBrush(ForeColor))
            {
                graphics.DrawString(labels[0], Font, brush, plot.Left + plot.Width / 2 - 40, ClientSize.Height - 20);
                var state = graphics.Save();
                graphics.TranslateTransform(14, plot.Top + plot.Height / 2 + 40);
                graphics.RotateTransform(-90);
                graphics.DrawString(labels[1], Font, brush, 0, 0);
                graphics.Restore(state);
            }
        }

        private PointF Project(StarSystem system)
        {
            var c = system.Coordinates;
            if (projection == RouteProjection.FrontXY) return new PointF((float)c.X, (float)c.Y);
            if (projection == RouteProjection.SideZY) return new PointF((float)c.Z, (float)c.Y);
            return new PointF((float)c.X, (float)c.Z);
        }

        private void ShowPointDetails(object sender, MouseEventArgs e)
        {
            var hit = hitTargets.LastOrDefault(x => x.Item1.Contains(e.Location));
            if (hit == null) { toolTip.Hide(this); lastTip = null; return; }
            var leg = hit.Item3 == 0 ? 0 : route[hit.Item3 - 1].Coordinates.DistanceTo(hit.Item2.Coordinates);
            var text = (hit.Item3 == 0 ? "Centre: " : "Stop " + hit.Item3 + ": ") + hit.Item2.Name
                     + "\nLeg: " + leg.ToString("0.00") + " ly"
                     + "\nCandidate score: " + hit.Item2.CandidateScore.ToString("0.0");
            if (text == lastTip) return;
            lastTip = text;
            toolTip.Show(text, this, e.X + 14, e.Y + 14, 5000);
        }

        private static Color ScoreColour(double score)
        {
            var ratio = Math.Max(0, Math.Min(1, score / 100));
            return Color.FromArgb(70, (int)(145 + 95 * ratio), (int)(210 - 80 * ratio));
        }

        private static void PadDomain(ref float min, ref float max)
        {
            var span = max - min;
            if (span < 0.001f) span = 2f;
            min -= span * 0.08f;
            max += span * 0.08f;
        }
    }
}
