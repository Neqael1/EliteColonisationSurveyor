using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using EliteColonisationSurveyor.Core;

namespace EliteColonisationSurveyor.Plugin
{
    internal sealed class EdsmClient
    {
        private static readonly HttpClient Http = CreateClient();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public async Task<List<StarSystem>> GetSphereAsync(string centre, double radius, CancellationToken token)
        {
            var url = "https://www.edsm.net/api-v1/sphere-systems?systemName=" + Uri.EscapeDataString(centre)
                    + "&radius=" + radius.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "&showCoordinates=1&showPermit=1&showInformation=1&showPrimaryStar=1";
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var rows = json.Deserialize<List<EdsmSystem>>(text) ?? new List<EdsmSystem>();
                var result = new List<StarSystem>();
                foreach (var row in rows)
                {
                    if (row.coords == null || string.IsNullOrWhiteSpace(row.name)) continue;
                    result.Add(new StarSystem {
                        Name = row.name, Id = row.id, DistanceFromCentre = row.distance,
                        Coordinates = new Coordinates { X = row.coords.x, Y = row.coords.y, Z = row.coords.z },
                        RequiresPermit = row.requirePermit,
                        Population = row.information?.population ?? 0,
                        Allegiance = row.information?.allegiance,
                        Government = row.information?.government,
                        Economy = row.information?.economy,
                        Security = row.information?.security,
                        PrimaryStarType = row.primaryStar?.type
                    });
                }
                return result;
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EliteColonisationSurveyor/0.1 (+https://github.com/EDDiscovery/EDDiscovery)");
            return client;
        }

        private sealed class EdsmSystem { public long id; public string name; public double distance; public Coords coords; public bool requirePermit; public Information information; public PrimaryStar primaryStar; }
        private sealed class Coords { public double x; public double y; public double z; }
        private sealed class Information { public long population; public string allegiance; public string government; public string economy; public string security; }
        private sealed class PrimaryStar { public string type; }
    }
}
