using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using EliteColonisationSurveyor.Core;

namespace EliteColonisationSurveyor.Plugin
{
    internal sealed class RouteMapControl : Control
    {
        private sealed class ProjectedPoint
        {
            public PointF Screen;
            public double Depth;
            public StarSystem System;
            public int Index;
        }

        private readonly ToolTip toolTip = new ToolTip();
        private IReadOnlyList<StarSystem> route = new List<StarSystem>();
        private readonly List<ProjectedPoint> hitTargets = new List<ProjectedPoint>();
        private double yaw = -0.65;
        private double pitch = 0.55;
        private double zoom = 1;
        private bool dragging;
        private Point dragStart;
        private string lastTip;

        public RouteMapControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(10, 16, 25);
            ForeColor = Color.Gainsboro;
            Resize += (_, __) => Invalidate();
            MouseDown += BeginRotation;
            MouseMove += RotateOrShowDetails;
            MouseUp += (_, __) => { dragging = false; Cursor = Cursors.Default; };
            MouseLeave += (_, __) => { dragging = false; Cursor = Cursors.Default; toolTip.Hide(this); lastTip = null; };
            MouseWheel += ZoomView;
        }

        public void SetRoute(IReadOnlyList<StarSystem> value)
        {
            route = value ?? new List<StarSystem>();
            Invalidate();
        }

        public void ResetView()
        {
            yaw = -0.65;
            pitch = 0.55;
            zoom = 1;
            Invalidate();
        }

        public void SetViewPreset(int preset)
        {
            if (preset == 1) { yaw = 0; pitch = 0; }
            else if (preset == 2) { yaw = Math.PI / 2; pitch = 0; }
            else { yaw = 0; pitch = Math.PI / 2; }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            hitTargets.Clear();

            var plot = new RectangleF(24, 24, Math.Max(10, ClientSize.Width - 48), Math.Max(10, ClientSize.Height - 48));
            using (var frame = new Pen(Color.FromArgb(75, 128, 160))) e.Graphics.DrawRectangle(frame, plot.X, plot.Y, plot.Width, plot.Height);
            if (route.Count == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(170, ForeColor)))
                    e.Graphics.DrawString("Generate a route to display its 3D map.", Font, brush, plot.X + 16, plot.Y + 16);
                return;
            }

            var centre = new Vector3(
                route.Average(x => x.Coordinates.X),
                route.Average(x => x.Coordinates.Y),
                route.Average(x => x.Coordinates.Z));
            var rotated = route.Select(x => Rotate(ToVector(x.Coordinates) - centre)).ToList();
            var extent = rotated.SelectMany(v => new[] { Math.Abs(v.X), Math.Abs(v.Y) }).DefaultIfEmpty(1).Max();
            if (extent < 0.001) extent = 1;
            var scale = Math.Min(plot.Width, plot.Height) * 0.42 / extent * zoom;
            var screenCentre = new PointF(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);

            DrawReferenceAxes(e.Graphics, screenCentre, scale, extent);

            var projected = rotated.Select((v, i) => new ProjectedPoint {
                Screen = new PointF(screenCentre.X + (float)(v.X * scale), screenCentre.Y - (float)(v.Y * scale)),
                Depth = v.Z, System = route[i], Index = i
            }).ToList();

            using (var routePen = new Pen(Color.FromArgb(65, 190, 255), 2f))
            {
                routePen.CustomEndCap = new AdjustableArrowCap(3, 5);
                for (var i = 1; i < projected.Count; i++) e.Graphics.DrawLine(routePen, projected[i - 1].Screen, projected[i].Screen);
            }

            foreach (var point in projected.OrderBy(x => x.Depth))
            {
                var perspective = (float)Math.Max(0.7, Math.Min(1.3, 1 + point.Depth / (extent * 8)));
                var radius = (point.Index == 0 ? 7f : 5f) * perspective;
                var rect = new RectangleF(point.Screen.X - radius, point.Screen.Y - radius, radius * 2, radius * 2);
                using (var brush = new SolidBrush(point.Index == 0 ? Color.Gold : ScoreColour(point.System.CandidateScore))) e.Graphics.FillEllipse(brush, rect);
                using (var outline = new Pen(Color.White, 1f)) e.Graphics.DrawEllipse(outline, rect);
                hitTargets.Add(new ProjectedPoint { Screen = point.Screen, Depth = point.Depth, System = point.System, Index = point.Index });
                if (point.Index == 0 || point.Index == projected.Count - 1)
                    using (var brush = new SolidBrush(ForeColor)) e.Graphics.DrawString(point.Index == 0 ? "Centre" : point.Index.ToString(), Font, brush, point.Screen.X + 7, point.Screen.Y - 15);
            }
        }

        private void DrawReferenceAxes(Graphics graphics, PointF origin, double scale, double extent)
        {
            var length = extent * 0.65;
            DrawAxis(graphics, origin, Rotate(new Vector3(length, 0, 0)), scale, "X", Color.FromArgb(230, 100, 100));
            DrawAxis(graphics, origin, Rotate(new Vector3(0, length, 0)), scale, "Y", Color.FromArgb(100, 220, 130));
            DrawAxis(graphics, origin, Rotate(new Vector3(0, 0, length)), scale, "Z", Color.FromArgb(100, 160, 255));
        }

        private static void DrawAxis(Graphics graphics, PointF origin, Vector3 axis, double scale, string label, Color color)
        {
            var end = new PointF(origin.X + (float)(axis.X * scale), origin.Y - (float)(axis.Y * scale));
            using (var pen = new Pen(Color.FromArgb(150, color), 1.5f)) graphics.DrawLine(pen, origin, end);
            using (var brush = new SolidBrush(color)) graphics.DrawString(label, SystemFonts.DefaultFont, brush, end.X + 3, end.Y + 3);
        }

        private void BeginRotation(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragStart = e.Location;
            Cursor = Cursors.SizeAll;
            toolTip.Hide(this);
        }

        private void RotateOrShowDetails(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                yaw += (e.X - dragStart.X) * 0.012;
                pitch = Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, pitch + (e.Y - dragStart.Y) * 0.012));
                dragStart = e.Location;
                Invalidate();
                return;
            }

            var hit = hitTargets.OrderByDescending(x => x.Depth)
                .FirstOrDefault(x => DistanceSquared(x.Screen, e.Location) <= 14 * 14);
            if (hit == null) { toolTip.Hide(this); lastTip = null; return; }
            var leg = hit.Index == 0 ? 0 : route[hit.Index - 1].Coordinates.DistanceTo(hit.System.Coordinates);
            var text = (hit.Index == 0 ? "Centre: " : "Stop " + hit.Index + ": ") + hit.System.Name
                     + "\nLeg: " + leg.ToString("0.00") + " ly"
                     + "\nCandidate score: " + hit.System.CandidateScore.ToString("0.0");
            if (text == lastTip) return;
            lastTip = text;
            toolTip.Show(text, this, e.X + 14, e.Y + 14, 5000);
        }

        private void ZoomView(object sender, MouseEventArgs e)
        {
            zoom = Math.Max(0.4, Math.Min(4, zoom * (e.Delta > 0 ? 1.12 : 0.89)));
            Invalidate();
        }

        private Vector3 Rotate(Vector3 value)
        {
            var cosY = Math.Cos(yaw); var sinY = Math.Sin(yaw);
            var x = value.X * cosY - value.Z * sinY;
            var z = value.X * sinY + value.Z * cosY;
            var cosP = Math.Cos(pitch); var sinP = Math.Sin(pitch);
            return new Vector3(x, value.Y * cosP - z * sinP, value.Y * sinP + z * cosP);
        }

        private static Vector3 ToVector(Coordinates c) => new Vector3(c.X, c.Y, c.Z);
        private static double DistanceSquared(PointF a, Point b) { var dx = a.X - b.X; var dy = a.Y - b.Y; return dx * dx + dy * dy; }
        private static Color ScoreColour(double score) { var ratio = Math.Max(0, Math.Min(1, score / 100)); return Color.FromArgb(70, (int)(145 + 95 * ratio), (int)(210 - 80 * ratio)); }

        private struct Vector3
        {
            public readonly double X, Y, Z;
            public Vector3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Vector3 operator -(Vector3 left, Vector3 right) => new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }
    }
}
