using System;
using System.Collections.Generic;
using System.Linq;
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

        public async Task EnrichBodiesAsync(IEnumerable<StarSystem> systems, CancellationToken token)
        {
            using (var gate = new SemaphoreSlim(6))
            {
                var tasks = systems.Select(async system => {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try { await EnrichBodyDataAsync(system, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* Missing or malformed EDSM body data remains explicitly unknown. */ }
                    finally { gate.Release(); }
                });
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        private async Task EnrichBodyDataAsync(StarSystem system, CancellationToken token)
        {
            var url = "https://www.edsm.net/api-system-v1/bodies?systemName=" + Uri.EscapeDataString(system.Name);
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return;
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = new JavaScriptSerializer().Deserialize<BodyResponse>(text);
                if (data == null || data.bodies == null || data.bodies.Count == 0) return;
                system.BodyDataAvailable = true;
                system.KnownBodyCount = data.bodyCount > 0 ? data.bodyCount : data.bodies.Count;
                system.BodyDataCompleteness = data.bodyCount > 0 ? Math.Min(1, data.bodies.Count / (double)data.bodyCount) : 1;
                var usefulDistances = new List<double>();
                foreach (var body in data.bodies.Where(x => string.Equals(x.type, "Planet", StringComparison.OrdinalIgnoreCase)))
                {
                    var subtype = body.subType ?? "";
                    var habitable = subtype.IndexOf("Earth-like", StringComparison.OrdinalIgnoreCase) >= 0
                                 || subtype.IndexOf("Water world", StringComparison.OrdinalIgnoreCase) >= 0
                                 || subtype.IndexOf("Ammonia world", StringComparison.OrdinalIgnoreCase) >= 0;
                    var terraformable = !string.IsNullOrWhiteSpace(body.terraformingState)
                                     && body.terraformingState.IndexOf("terraformable", StringComparison.OrdinalIgnoreCase) >= 0
                                     && body.terraformingState.IndexOf("not terraformable", StringComparison.OrdinalIgnoreCase) < 0;
                    var resource = subtype.IndexOf("Metal-rich", StringComparison.OrdinalIgnoreCase) >= 0
                                || subtype.IndexOf("High metal content", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (habitable) system.HabitableBodyCount++;
                    if (terraformable) system.TerraformableBodyCount++;
                    if (body.isLandable) system.LandableBodyCount++;
                    if (resource) system.ResourceBodyCount++;
                    if (body.rings != null && (string.Equals(body.reserveLevel, "Pristine", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(body.reserveLevel, "Major", StringComparison.OrdinalIgnoreCase)))
                        system.ValuableRingCount += body.rings.Count;
                    if ((habitable || terraformable || resource || body.isLandable) && body.distanceToArrival > 0)
                        usefulDistances.Add(body.distanceToArrival);
                }
                system.NearestUsefulArrivalLs = usefulDistances.Count > 0 ? usefulDistances.Min() : 0;
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
        private sealed class BodyResponse { public int bodyCount; public List<Body> bodies; }
        private sealed class Body { public string type; public string subType; public double distanceToArrival; public bool isLandable; public string terraformingState; public string reserveLevel; public List<Ring> rings; }
        private sealed class Ring { public string type; }
    }
}
