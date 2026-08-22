using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly Dictionary<string, List<StarSystem>> sphereCache = new Dictionary<string, List<StarSystem>>();
        private readonly object cacheSync = new object();

        public async Task<StarSystem> GetSystemAsync(string name, CancellationToken token)
        {
            var url = "https://www.edsm.net/api-v1/system?systemName=" + Uri.EscapeDataString(name)
                    + "&showId=1&showCoordinates=1&showPermit=1&showInformation=1&showPrimaryStar=1";
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var row = json.Deserialize<EdsmSystem>(text);
                return ToStarSystem(row);
            }
        }

        public async Task CheckCataloguePresenceAsync(IEnumerable<StarSystem> systems, IProgress<int> progress, CancellationToken token)
        {
            var completed = 0;
            using (var gate = new SemaphoreSlim(6))
            {
                var tasks = systems.Select(async system => {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        var edsmListed = await IsListedInEdsmAsync(system, token).ConfigureAwait(false);
                        var spanshListed = await IsListedInSpanshAsync(system, token).ConfigureAwait(false);
                        system.CataloguePresence = !edsmListed.HasValue || !spanshListed.HasValue ? CataloguePresence.Unknown
                            : edsmListed.Value && spanshListed.Value ? CataloguePresence.EdsmAndSpansh
                            : edsmListed.Value ? CataloguePresence.EdsmOnly
                            : spanshListed.Value ? CataloguePresence.SpanshOnly
                            : CataloguePresence.NotListed;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { system.CataloguePresence = CataloguePresence.Unknown; }
                    finally
                    {
                        system.CatalogueLookupAttempted = true;
                        gate.Release();
                        progress?.Report(Interlocked.Increment(ref completed));
                    }
                });
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        private async Task<bool?> IsListedInEdsmAsync(StarSystem system, CancellationToken token)
        {
            var url = "https://www.edsm.net/api-v1/system?systemName=" + Uri.EscapeDataString(system.Name) + "&showId=1";
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return response.StatusCode == HttpStatusCode.NotFound ? false : (bool?)null;
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                try {
                    var row = json.Deserialize<EdsmSystem>(text);
                    return row != null && !string.IsNullOrWhiteSpace(row.name);
                }
                catch { return text.Trim() == "[]" || text.Trim() == "{}" ? false : (bool?)null; }
            }
        }

        private static async Task<bool?> IsListedInSpanshAsync(StarSystem system, CancellationToken token)
        {
            if (system.SystemAddress <= 0) return null;
            using (var response = await Http.GetAsync("https://www.spansh.co.uk/api/system/" + system.SystemAddress, token).ConfigureAwait(false))
                return response.IsSuccessStatusCode ? true : response.StatusCode == HttpStatusCode.NotFound ? false : (bool?)null;
        }

        public async Task<List<StarSystem>> GetSphereAsync(string centre, double radius, CancellationToken token)
        {
            var url = "https://www.edsm.net/api-v1/sphere-systems?systemName=" + Uri.EscapeDataString(centre)
                    + "&radius=" + radius.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "&showId=1&showCoordinates=1&showPermit=1&showInformation=1&showPrimaryStar=1";
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var rows = json.Deserialize<List<EdsmSystem>>(text) ?? new List<EdsmSystem>();
                var result = new List<StarSystem>();
                foreach (var row in rows)
                {
                    var system = ToStarSystem(row);
                    if (system != null) result.Add(system);
                }
                return result;
            }
        }

        public async Task<List<StarSystem>> GetSphereAtCoordinatesAsync(Coordinates centre, double radius, CancellationToken token)
        {
            if (centre == null) throw new ArgumentNullException(nameof(centre));
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var key = Math.Round(centre.X, 1).ToString(culture) + ":" + Math.Round(centre.Y, 1).ToString(culture)
                    + ":" + Math.Round(centre.Z, 1).ToString(culture) + ":" + radius.ToString(culture);
            lock (cacheSync)
            {
                List<StarSystem> cached;
                if (sphereCache.TryGetValue(key, out cached)) return cached.ToList();
            }
            var url = "https://www.edsm.net/api-v1/sphere-systems?x=" + centre.X.ToString(culture)
                    + "&y=" + centre.Y.ToString(culture) + "&z=" + centre.Z.ToString(culture)
                    + "&radius=" + radius.ToString(culture)
                    + "&showId=1&showCoordinates=1&showPermit=1&showPrimaryStar=1";
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var rows = json.Deserialize<List<EdsmSystem>>(text) ?? new List<EdsmSystem>();
                var result = rows.Select(ToStarSystem).Where(x => x != null).ToList();
                lock (cacheSync) sphereCache[key] = result;
                return result.ToList();
            }
        }

        private static StarSystem ToStarSystem(EdsmSystem row)
        {
            if (row == null || row.coords == null || string.IsNullOrWhiteSpace(row.name)) return null;
            return new StarSystem {
                Name = row.name, Id = row.id, SystemAddress = row.id64, DistanceFromCentre = row.distance,
                Coordinates = new Coordinates { X = row.coords.x, Y = row.coords.y, Z = row.coords.z },
                RequiresPermit = row.requirePermit,
                Population = row.information?.population ?? 0,
                Allegiance = row.information?.allegiance,
                Government = row.information?.government,
                Economy = row.information?.economy,
                Security = row.information?.security,
                PrimaryStarType = row.primaryStar?.type
            };
        }

        public Task EnrichBodiesAsync(IEnumerable<StarSystem> systems, CancellationToken token)
            => EnrichBodiesAsync(systems, ExplorationDataSource.Edsm, null, token);

        public async Task EnrichBodiesAsync(IEnumerable<StarSystem> systems, ExplorationDataSource source, CancellationToken token)
            => await EnrichBodiesAsync(systems, source, null, token).ConfigureAwait(false);

        public async Task EnrichBodiesAsync(IEnumerable<StarSystem> systems, ExplorationDataSource source,
            IProgress<int> progress, CancellationToken token)
        {
            var completed = 0;
            using (var gate = new SemaphoreSlim(6))
            {
                var tasks = systems.Select(async system => {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try { await EnrichBodyDataAsync(system, source, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* Missing or malformed EDSM body data remains explicitly unknown. */ }
                    finally {
                        gate.Release();
                        progress?.Report(Interlocked.Increment(ref completed));
                    }
                });
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        private async Task EnrichBodyDataAsync(StarSystem system, ExplorationDataSource source, CancellationToken token)
        {
            var primary = source == ExplorationDataSource.Spansh
                ? await FetchSpanshBodiesAsync(system, token).ConfigureAwait(false)
                : await FetchEdsmBodiesAsync(system, token).ConfigureAwait(false);
            var crossReference = source == ExplorationDataSource.Spansh
                ? await FetchEdsmBodiesAsync(system, token).ConfigureAwait(false)
                : null;
            system.BodyDataLookupSucceeded = primary != null && (source != ExplorationDataSource.Spansh || crossReference != null);
            if (!system.BodyDataLookupSucceeded) return;

            var snapshots = crossReference == null ? new[] { primary } : new[] { primary, crossReference };
            var bodies = snapshots.SelectMany(x => x.Bodies)
                .GroupBy(x => x.Name ?? "", StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            system.ExpectedBodyCount = snapshots.Max(x => x.ExpectedBodyCount);
            system.KnownBodyCount = bodies.Count;
            system.BodyDataAvailable = system.KnownBodyCount > 0;
            system.BodyDataCompleteness = system.ExpectedBodyCount > 0
                ? Math.Min(1, system.KnownBodyCount / (double)system.ExpectedBodyCount)
                : system.BodyDataAvailable ? 1 : 0;
            ApplyBodyDetails(system, bodies);
        }

        private async Task<BodySnapshot> FetchEdsmBodiesAsync(StarSystem system, CancellationToken token)
        {
            var url = "https://www.edsm.net/api-system-v1/bodies?systemName=" + Uri.EscapeDataString(system.Name);
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new BodySnapshot();
                if (!response.IsSuccessStatusCode) return null;
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = new JavaScriptSerializer().Deserialize<BodyResponse>(text);
                if (data == null) return null;
                return new BodySnapshot {
                    ExpectedBodyCount = data.bodyCount,
                    Bodies = (data.bodies ?? new List<Body>()).Select(x => new BodyInfo {
                        Name = x.name, Type = x.type, SubType = x.subType, DistanceToArrival = x.distanceToArrival,
                        IsLandable = x.isLandable, TerraformingState = x.terraformingState,
                        ReserveLevel = x.reserveLevel, RingCount = x.rings?.Count ?? 0
                    }).ToList()
                };
            }
        }

        private async Task<BodySnapshot> FetchSpanshBodiesAsync(StarSystem system, CancellationToken token)
        {
            if (system.SystemAddress <= 0) return null;
            var url = "https://www.spansh.co.uk/api/system/" + system.SystemAddress;
            using (var response = await Http.GetAsync(url, token).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new BodySnapshot();
                if (!response.IsSuccessStatusCode) return null;
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = new JavaScriptSerializer().Deserialize<SpanshResponse>(text)?.record;
                if (data == null) return null;
                return new BodySnapshot {
                    ExpectedBodyCount = data.body_count,
                    Bodies = (data.bodies ?? new List<SpanshBody>()).Select(x => new BodyInfo {
                        Name = x.name, Type = x.type, SubType = x.subtype, DistanceToArrival = x.distance_to_arrival,
                        IsLandable = x.is_landable, TerraformingState = x.terraforming_state,
                        ReserveLevel = x.reserve_level, RingCount = x.rings?.Count ?? 0
                    }).ToList()
                };
            }
        }

        private static void ApplyBodyDetails(StarSystem system, List<BodyInfo> bodies)
        {
            var usefulDistances = new List<double>();
            foreach (var body in bodies.Where(x => string.Equals(x.Type, "Planet", StringComparison.OrdinalIgnoreCase)))
            {
                var subtype = body.SubType ?? "";
                var habitable = subtype.IndexOf("Earth-like", StringComparison.OrdinalIgnoreCase) >= 0
                             || subtype.IndexOf("Water world", StringComparison.OrdinalIgnoreCase) >= 0
                             || subtype.IndexOf("Ammonia world", StringComparison.OrdinalIgnoreCase) >= 0;
                var terraformable = !string.IsNullOrWhiteSpace(body.TerraformingState)
                                 && body.TerraformingState.IndexOf("terraformable", StringComparison.OrdinalIgnoreCase) >= 0
                                 && body.TerraformingState.IndexOf("not terraformable", StringComparison.OrdinalIgnoreCase) < 0;
                var resource = subtype.IndexOf("Metal-rich", StringComparison.OrdinalIgnoreCase) >= 0
                            || subtype.IndexOf("High metal content", StringComparison.OrdinalIgnoreCase) >= 0;
                if (habitable) system.HabitableBodyCount++;
                if (terraformable) system.TerraformableBodyCount++;
                if (body.IsLandable) system.LandableBodyCount++;
                if (resource) system.ResourceBodyCount++;
                if (body.RingCount > 0 && (string.Equals(body.ReserveLevel, "Pristine", StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(body.ReserveLevel, "Major", StringComparison.OrdinalIgnoreCase)))
                    system.ValuableRingCount += body.RingCount;
                if ((habitable || terraformable || resource || body.IsLandable) && body.DistanceToArrival > 0)
                    usefulDistances.Add(body.DistanceToArrival);
            }
            system.NearestUsefulArrivalLs = usefulDistances.Count > 0 ? usefulDistances.Min() : 0;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EliteColonisationSurveyor/0.1 (+https://github.com/EDDiscovery/EDDiscovery)");
            return client;
        }

        private sealed class EdsmSystem { public long id; public long id64; public string name; public double distance; public Coords coords; public bool requirePermit; public Information information; public PrimaryStar primaryStar; }
        private sealed class Coords { public double x; public double y; public double z; }
        private sealed class Information { public long population; public string allegiance; public string government; public string economy; public string security; }
        private sealed class PrimaryStar { public string type; }
        private sealed class BodyResponse { public int bodyCount; public List<Body> bodies; }
        private sealed class Body { public string name; public string type; public string subType; public double distanceToArrival; public bool isLandable; public string terraformingState; public string reserveLevel; public List<Ring> rings; }
        private sealed class Ring { public string type; }
        private sealed class SpanshResponse { public SpanshRecord record; }
        private sealed class SpanshRecord { public int body_count; public List<SpanshBody> bodies; }
        private sealed class SpanshBody { public string name; public string type; public string subtype; public double distance_to_arrival; public bool is_landable; public string terraforming_state; public string reserve_level; public List<Ring> rings; }
        private sealed class BodySnapshot { public int ExpectedBodyCount; public List<BodyInfo> Bodies = new List<BodyInfo>(); }
        private sealed class BodyInfo { public string Name; public string Type; public string SubType; public double DistanceToArrival; public bool IsLandable; public string TerraformingState; public string ReserveLevel; public int RingCount; }
    }
}
