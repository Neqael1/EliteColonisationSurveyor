using System;
using System.Collections.Generic;
using System.Linq;

namespace EliteColonisationSurveyor.Core
{
    public sealed class DirectionCandidate
    {
        public Coordinates Target { get; set; }
        public double MeanDensity { get; set; }
        public double MinimumDensity { get; set; }
        public double TrafficPenalty { get; set; }
        public double GeometryScore { get; set; }
        public double ExternalDensity { get; set; }
        public double ExternalUnexplored { get; set; }
        public bool ExternalDataAvailable { get; set; }
    }

    public sealed class AnchorSample
    {
        public StarSystem Anchor { get; set; }
        public Coordinates Target { get; set; }
        public int EndpointKnownSystems { get; set; }
        public IReadOnlyList<int> CorridorKnownSystems { get; set; } = new List<int>();
        public int FailedSamples { get; set; }
        public double DirectionScore { get; set; }
        public double ExpectedDensity { get; set; }
        public double ExplorationPotential { get; set; }
        public bool UsedExternalMap { get; set; }
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
            return GenerateDirectionCandidates(origin, distanceLy, count)
                .Take(Math.Max(4, count)).Select(x => x.Target).ToList();
        }

        public IReadOnlyList<DirectionCandidate> GenerateDirectionCandidates(Coordinates origin, double distanceLy, int requestedCount)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            requestedCount = Math.Max(4, requestedCount);
            distanceLy = Math.Max(1, distanceLy);
            var candidateCount = requestedCount * 5;
            var result = new List<DirectionCandidate>();
            var verticalExtent = Math.Min(800, distanceLy * 0.15);
            var verticalBands = new[] { -1.0, -0.5, 0.5, 1.0, 0.0 };
            var centreBearing = Math.Atan2(GalacticCentreZ - origin.Z, GalacticCentreX - origin.X);
            var goldenAngle = Math.PI * (3 - Math.Sqrt(5));
            for (var i = 0; i < candidateCount; i++)
            {
                var y = verticalExtent * verticalBands[i % verticalBands.Length];
                var horizontal = Math.Sqrt(Math.Max(0, distanceLy * distanceLy - y * y));
                // Half the candidates explore offsets around the denser inward direction;
                // the remainder retain full-azimuth coverage for commanders elsewhere.
                var angle = i < candidateCount / 2
                    ? centreBearing + SignedOffset(i) * Math.PI / 180.0
                    : centreBearing + i * goldenAngle;
                var target = new Coordinates {
                    X = origin.X + horizontal * Math.Cos(angle), Y = origin.Y + y,
                    Z = origin.Z + horizontal * Math.Sin(angle)
                };
                result.Add(AssessDirection(origin, target));
            }
            return result.OrderByDescending(x => x.GeometryScore).ToList();
        }

        public DirectionCandidate AssessDirection(Coordinates origin, Coordinates target)
        {
            var points = GenerateCorridorSamples(origin, target, 16).Skip(3).ToList();
            var densities = points.Select(EstimateDensity).ToList();
            var mean = densities.Average();
            var minimum = densities.Min();
            var traffic = points.Average(KnownRoutePenalty);
            return new DirectionCandidate {
                Target = target, MeanDensity = mean, MinimumDensity = minimum, TrafficPenalty = traffic,
                GeometryScore = mean * 0.55 + minimum * 0.30 - traffic * 0.15
            };
        }

        public static double CombinedDirectionScore(DirectionCandidate candidate)
        {
            if (candidate == null) return double.MinValue;
            if (!candidate.ExternalDataAvailable) return candidate.GeometryScore;
            return candidate.GeometryScore * 0.35 + candidate.ExternalDensity * 0.30
                 + candidate.ExternalUnexplored * 0.35;
        }

        private const double GalacticCentreX = 25.2;
        private const double GalacticCentreZ = 25899.97;

        private static double SignedOffset(int index)
        {
            var offsets = new[] { 12.0, -12.0, 24.0, -24.0, 38.0, -38.0, 55.0, -55.0, 75.0, -75.0 };
            return offsets[index % offsets.Length];
        }

        private static double EstimateDensity(Coordinates point)
        {
            var dx = point.X - GalacticCentreX;
            var dz = point.Z - GalacticCentreZ;
            var radius = Math.Sqrt(dx * dx + dz * dz);
            var scaleHeight = 300 + 1700 * Math.Exp(-radius / 7000.0);
            var vertical = Math.Exp(-Math.Abs(point.Y + 20.9) / scaleHeight);
            var disc = Math.Exp((26000 - radius) / 16000.0) * 0.22;
            var bulge = 0.9 * Math.Exp(-radius / 4500.0);
            var theta = Math.Atan2(dz, dx);
            var armPhase = theta - Math.Log(Math.Max(1000, radius) / 26000.0) / Math.Tan(12 * Math.PI / 180.0);
            var nearestArm = Math.Abs(NormaliseAngle(armPhase - Math.Atan2(-GalacticCentreZ, -GalacticCentreX), Math.PI / 2));
            var armWidth = Math.Max(0.08, 1400 / Math.Max(6000, radius));
            var armBoost = 0.35 * Math.Exp(-(nearestArm * nearestArm) / (2 * armWidth * armWidth));
            return Math.Max(0, Math.Min(1, (disc + bulge + armBoost) * vertical));
        }

        private static double KnownRoutePenalty(Coordinates point)
        {
            var sol = new Coordinates();
            var centre = new Coordinates { X = GalacticCentreX, Y = -20.9, Z = GalacticCentreZ };
            var colonia = new Coordinates { X = -9530.5, Y = -910.3, Z = 19808.1 };
            var distance = Math.Min(DistanceToSegment(point, sol, centre), DistanceToSegment(point, sol, colonia));
            return Math.Exp(-distance / 500.0);
        }

        private static double DistanceToSegment(Coordinates point, Coordinates start, Coordinates end)
        {
            var vx = end.X - start.X; var vy = end.Y - start.Y; var vz = end.Z - start.Z;
            var wx = point.X - start.X; var wy = point.Y - start.Y; var wz = point.Z - start.Z;
            var lengthSquared = vx * vx + vy * vy + vz * vz;
            var t = lengthSquared > 0 ? Math.Max(0, Math.Min(1, (wx * vx + wy * vy + wz * vz) / lengthSquared)) : 0;
            var nearest = new Coordinates { X = start.X + vx * t, Y = start.Y + vy * t, Z = start.Z + vz * t };
            return point.DistanceTo(nearest);
        }

        private static double NormaliseAngle(double angle, double period)
        {
            angle %= period;
            if (angle > period / 2) angle -= period;
            if (angle < -period / 2) angle += period;
            return angle;
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
                var score = endpointTerm * 0.35 + medianTerm * 0.35 + busiestTerm * 0.20
                          + unsafeAnchor * 0.10 - Math.Max(0, sample.DirectionScore) * 0.80;
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
                    Explanation = (sample.UsedExternalMap ? "Map-refined" : "Geometry")
                        + " direction " + sample.DirectionScore.ToString("0.00")
                        + ", density " + sample.ExpectedDensity.ToString("0.00")
                        + (sample.UsedExternalMap ? ", unexplored " + sample.ExplorationPotential.ToString("0.00") : "")
                        + "; endpoint " + sample.EndpointKnownSystems + " known; corridor median "
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
