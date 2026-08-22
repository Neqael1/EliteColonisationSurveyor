using System;
using System.Collections.Generic;
using System.Linq;

namespace EliteColonisationSurveyor.Core
{
    public sealed class AnchorSample
    {
        public StarSystem Anchor { get; set; }
        public Coordinates Target { get; set; }
        public int EndpointKnownSystems { get; set; }
        public IReadOnlyList<int> CorridorKnownSystems { get; set; } = new List<int>();
        public int FailedSamples { get; set; }
    }

    public sealed class AnchorRecommendation
    {
        public StarSystem Anchor { get; set; }
        public double DistanceLy { get; set; }
        public int EndpointKnownSystems { get; set; }
        public double MedianCorridorSystems { get; set; }
        public int BusiestCorridorSystems { get; set; }
        public double CongestionScore { get; set; }
        public string Confidence { get; set; }
        public string Explanation { get; set; }
    }

    public sealed class AnchorRecommender
    {
        public IReadOnlyList<Coordinates> GenerateTargets(Coordinates origin, double distanceLy, int count)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            count = Math.Max(4, count);
            distanceLy = Math.Max(1, distanceLy);
            var result = new List<Coordinates>();
            var goldenAngle = Math.PI * (3 - Math.Sqrt(5));
            for (var i = 0; i < count; i++)
            {
                var y = 1 - 2 * (i + 0.5) / count;
                var horizontal = Math.Sqrt(Math.Max(0, 1 - y * y));
                var angle = i * goldenAngle;
                result.Add(new Coordinates {
                    X = origin.X + distanceLy * horizontal * Math.Cos(angle),
                    Y = origin.Y + distanceLy * y,
                    Z = origin.Z + distanceLy * horizontal * Math.Sin(angle)
                });
            }
            return result;
        }

        public IReadOnlyList<Coordinates> GenerateCorridorSamples(Coordinates origin, Coordinates target, int count)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (target == null) throw new ArgumentNullException(nameof(target));
            count = Math.Max(1, count);
            return Enumerable.Range(1, count).Select(i => {
                var fraction = i / (double)(count + 1);
                return new Coordinates {
                    X = origin.X + (target.X - origin.X) * fraction,
                    Y = origin.Y + (target.Y - origin.Y) * fraction,
                    Z = origin.Z + (target.Z - origin.Z) * fraction
                };
            }).ToList();
        }

        public IReadOnlyList<AnchorRecommendation> Rank(Coordinates origin, IEnumerable<AnchorSample> samples, int maximum)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var ranked = new List<AnchorRecommendation>();
            foreach (var sample in samples.Where(x => x?.Anchor?.Coordinates != null))
            {
                var corridor = (sample.CorridorKnownSystems ?? new List<int>()).OrderBy(x => x).ToList();
                if (corridor.Count == 0) continue;
                var median = corridor.Count % 2 == 1 ? corridor[corridor.Count / 2]
                    : (corridor[corridor.Count / 2 - 1] + corridor[corridor.Count / 2]) / 2.0;
                var busiest = corridor[corridor.Count - 1];
                var endpointTerm = Math.Log(1 + Math.Max(0, sample.EndpointKnownSystems));
                var medianTerm = Math.Log(1 + median);
                var busiestTerm = Math.Log(1 + busiest);
                var unsafeAnchor = sample.Anchor.RequiresPermit ? 4 : IsScoopable(sample.Anchor.PrimaryStarType) ? 0 : 0.5;
                var score = endpointTerm * 0.35 + medianTerm * 0.35 + busiestTerm * 0.20 + unsafeAnchor * 0.10;
                var zeroSamples = corridor.Count(x => x == 0);
                var confidence = sample.FailedSamples > 0 ? "Low"
                    : zeroSamples > corridor.Count / 2 || sample.EndpointKnownSystems < 2 ? "Low"
                    : zeroSamples > 0 ? "Medium" : "High";
                ranked.Add(new AnchorRecommendation {
                    Anchor = sample.Anchor,
                    DistanceLy = origin.DistanceTo(sample.Anchor.Coordinates),
                    EndpointKnownSystems = sample.EndpointKnownSystems,
                    MedianCorridorSystems = median,
                    BusiestCorridorSystems = busiest,
                    CongestionScore = Math.Round(score, 3),
                    Confidence = confidence,
                    Explanation = "Endpoint " + sample.EndpointKnownSystems + " known; corridor median "
                        + median.ToString("0.#") + ", busiest sample " + busiest
                        + (sample.FailedSamples > 0 ? "; " + sample.FailedSamples + " lookup failure(s)" : "")
                });
            }
            return ranked.OrderBy(x => x.CongestionScore).ThenByDescending(x => ConfidenceRank(x.Confidence))
                .ThenByDescending(x => x.DistanceLy).Take(Math.Max(1, maximum)).ToList();
        }

        private static int ConfidenceRank(string confidence)
            => confidence == "High" ? 3 : confidence == "Medium" ? 2 : 1;

        private static bool IsScoopable(string starType)
        {
            if (string.IsNullOrWhiteSpace(starType)) return false;
            var token = starType.Trim().Split(' ')[0];
            return "OBAFGKM".IndexOf(token.Length > 0 ? char.ToUpperInvariant(token[0]) : '?') >= 0;
        }
    }
}
