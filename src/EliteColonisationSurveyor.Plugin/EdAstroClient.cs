using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EliteColonisationSurveyor.Core;

namespace EliteColonisationSurveyor.Plugin
{
    internal sealed class EdAstroSample
    {
        public double Density { get; set; }
        public double Unexplored { get; set; }
        public bool Available { get; set; }
    }

    internal sealed class EdAstroClient
    {
        private const int Zoom = 5;
        private static readonly HttpClient Http = CreateClient();
        private readonly Dictionary<string, Bitmap> tiles = new Dictionary<string, Bitmap>();
        private readonly object sync = new object();
        private string tileTimestamp;

        public async Task<EdAstroSample> SampleAsync(Coordinates point, CancellationToken token)
        {
            if (point == null) throw new ArgumentNullException(nameof(point));
            var scale = 1 << Zoom;
            var longitude = point.X / 640.0 + 128;
            var latitude = point.Z / 640.0 - 167.0625;
            var pixelX = longitude * scale;
            var pixelY = -latitude * scale;
            var tileX = (int)Math.Floor(pixelX / 256);
            var tileY = (int)Math.Floor(pixelY / 256);
            if (tileX < 0 || tileY < 0 || tileX >= scale || tileY >= scale) return new EdAstroSample();
            var localX = Math.Max(0, Math.Min(255, (int)Math.Floor(pixelX - tileX * 256)));
            var localY = Math.Max(0, Math.Min(255, (int)Math.Floor(pixelY - tileY * 256)));
            var densityTile = await GetTileAsync("galaxy", tileX, tileY, token).ConfigureAwait(false);
            var saturationTile = await GetTileAsync("saturation", tileX, tileY, token).ConfigureAwait(false);
            var densityColour = AverageColour(densityTile, localX, localY, 2);
            var saturationColour = AverageColour(saturationTile, localX, localY, 2);
            var density = (densityColour.R + densityColour.G + densityColour.B) / 765.0;
            var unexplored = (saturationColour.B + 255.0 - saturationColour.R) / 510.0;
            return new EdAstroSample {
                Density = Math.Max(0, Math.Min(1, density)),
                Unexplored = Math.Max(0, Math.Min(1, unexplored)),
                Available = density > 0.005
            };
        }

        private async Task<Bitmap> GetTileAsync(string layer, int x, int y, CancellationToken token)
        {
            var timestamp = await GetTimestampAsync(token).ConfigureAwait(false);
            var key = layer + ":" + Zoom + ":" + x + ":" + y + ":" + timestamp;
            lock (sync) { Bitmap cached; if (tiles.TryGetValue(key, out cached)) return cached; }
            var url = "https://edastro.com/galmap/tiles/" + layer + "/" + Zoom + "/" + x + "/" + y + ".png?" + Uri.EscapeDataString(timestamp);
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                Bitmap bitmap;
                using (var stream = new MemoryStream(bytes))
                using (var source = Image.FromStream(stream)) bitmap = new Bitmap(source);
                lock (sync) tiles[key] = bitmap;
                return bitmap;
            }
        }

        private async Task<string> GetTimestampAsync(CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(tileTimestamp)) return tileTimestamp;
            using (var response = await Http.GetAsync("https://edastro.com/galmap/tiles-timestamp", token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                tileTimestamp = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
                return tileTimestamp;
            }
        }

        private static Color AverageColour(Bitmap bitmap, int x, int y, int radius)
        {
            long red = 0, green = 0, blue = 0, count = 0;
            for (var px = Math.Max(0, x - radius); px <= Math.Min(bitmap.Width - 1, x + radius); px++)
            for (var py = Math.Max(0, y - radius); py <= Math.Min(bitmap.Height - 1, y + radius); py++)
            {
                var colour = bitmap.GetPixel(px, py);
                red += colour.R; green += colour.G; blue += colour.B; count++;
            }
            return count == 0 ? Color.Black : Color.FromArgb((int)(red / count), (int)(green / count), (int)(blue / count));
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 EliteColonisationSurveyor/0.14");
            client.DefaultRequestHeaders.Referrer = new Uri("https://edastro.com/galmap/");
            return client;
        }
    }
}
