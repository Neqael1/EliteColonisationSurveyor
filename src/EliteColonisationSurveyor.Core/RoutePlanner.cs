using System;
using System.Collections.Generic;
using System.Linq;

namespace EliteColonisationSurveyor.Core
{
    public sealed class RoutePlanner
    {
        public IReadOnlyList<StarSystem> Plan(StarSystem origin, IEnumerable<StarSystem> candidates, double jumpRange)
            => Plan(origin, candidates, jumpRange, SearchPattern.ShortestRoute);

        public IReadOnlyList<StarSystem> Plan(StarSystem origin, IEnumerable<StarSystem> candidates, double jumpRange, SearchPattern pattern)
        {
            if (origin == null || origin.Coordinates == null) throw new ArgumentNullException(nameof(origin));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var remaining = candidates.Where(x => x != null && x.Coordinates != null && x.Name != origin.Name).ToList();
            if (pattern == SearchPattern.ScoreFirst)
                return ConstrainToJumpRange(WithOrigin(origin, remaining.OrderByDescending(x => x.CandidateScore).ThenBy(x => x.DistanceFromCentre)), jumpRange);
            if (pattern == SearchPattern.BoundarySurvey)
                return ConstrainToJumpRange(WithOrigin(origin, remaining.OrderByDescending(x => x.DistanceFromCentre).ThenByDescending(x => x.CandidateScore)), jumpRange);
            if (pattern == SearchPattern.ConcentricShells)
                return ConstrainToJumpRange(PlanShells(origin, remaining, jumpRange), jumpRange);
            if (pattern == SearchPattern.Spiral3D)
                return ConstrainToJumpRange(PlanSpiral(origin, remaining), jumpRange);
            if (pattern == SearchPattern.OctantSweep)
                return ConstrainToJumpRange(PlanOctants(origin, remaining, jumpRange), jumpRange);
            if (pattern == SearchPattern.JumpSafe)
                return PlanJumpSafe(origin, remaining, jumpRange);

            var route = new List<StarSystem> { origin };
            var current = origin;

            while (remaining.Count > 0)
            {
                var next = pattern == SearchPattern.Balanced
                    ? remaining.OrderBy(x => BalancedCost(current, x, route, jumpRange)).First()
                    : remaining.OrderBy(x => PenalisedDistance(current, x, jumpRange)).ThenByDescending(x => x.CandidateScore).First();
                route.Add(next);
                remaining.Remove(next);
                current = next;
            }

            ImproveWithTwoOpt(route, 8);
            return ConstrainToJumpRange(route, jumpRange);
        }

        private static IReadOnlyList<StarSystem> ConstrainToJumpRange(IReadOnlyList<StarSystem> preferredRoute, double jumpRange)
        {
            if (jumpRange <= 0 || preferredRoute.Count < 2) return preferredRoute;
            var safe = new List<StarSystem> { preferredRoute[0] };
            var remaining = preferredRoute.Skip(1).Select((system, index) => new { system, index }).ToList();
            while (remaining.Count > 0)
            {
                var current = safe[safe.Count - 1];
                var reachable = remaining
                    .Where(x => current.Coordinates.DistanceTo(x.system.Coordinates) <= jumpRange + 0.001)
                    .OrderBy(x => x.index)
                    .ThenBy(x => current.Coordinates.DistanceTo(x.system.Coordinates))
                    .FirstOrDefault();
                if (reachable == null) break;
                safe.Add(reachable.system);
                remaining.Remove(reachable);
            }
            return safe;
        }

        private static IReadOnlyList<StarSystem> PlanShells(StarSystem origin, List<StarSystem> candidates, double jumpRange)
        {
            var width = Math.Max(5, jumpRange > 0 ? jumpRange : 20);
            var route = new List<StarSystem> { origin };
            foreach (var shell in candidates.GroupBy(x => (int)Math.Floor(x.DistanceFromCentre / width)).OrderBy(x => x.Key))
                AppendNearest(route, shell.ToList(), jumpRange);
            return route;
        }

        private static IReadOnlyList<StarSystem> PlanSpiral(StarSystem origin, List<StarSystem> candidates)
        {
            const double turns = 4;
            var ordered = candidates.OrderBy(x => SpiralKey(origin, x, turns)).ThenBy(x => x.DistanceFromCentre);
            return WithOrigin(origin, ordered);
        }

        private static IReadOnlyList<StarSystem> PlanOctants(StarSystem origin, List<StarSystem> candidates, double jumpRange)
        {
            var route = new List<StarSystem> { origin };
            foreach (var octant in candidates.GroupBy(x => Octant(origin, x)).OrderBy(x => x.Key))
                AppendNearest(route, octant.ToList(), jumpRange);
            return route;
        }

        private static IReadOnlyList<StarSystem> PlanJumpSafe(StarSystem origin, List<StarSystem> candidates, double jumpRange)
        {
            if (jumpRange <= 0) return new RoutePlanner().Plan(origin, candidates, jumpRange, SearchPattern.ShortestRoute);
            var route = new List<StarSystem> { origin };
            var current = origin;
            while (candidates.Count > 0)
            {
                var next = candidates.Where(x => current.Coordinates.DistanceTo(x.Coordinates) <= jumpRange + 0.001)
                    .OrderByDescending(x => x.CandidateScore).ThenBy(x => current.Coordinates.DistanceTo(x.Coordinates)).FirstOrDefault();
                if (next == null) break;
                route.Add(next); candidates.Remove(next); current = next;
            }
            return route;
        }

        private static void AppendNearest(List<StarSystem> route, List<StarSystem> remaining, double jumpRange)
        {
            while (remaining.Count > 0)
            {
                var current = route[route.Count - 1];
                var next = remaining.OrderBy(x => PenalisedDistance(current, x, jumpRange)).ThenByDescending(x => x.CandidateScore).First();
                route.Add(next); remaining.Remove(next);
            }
        }

        private static IReadOnlyList<StarSystem> WithOrigin(StarSystem origin, IEnumerable<StarSystem> ordered)
        {
            var result = new List<StarSystem> { origin };
            result.AddRange(ordered);
            return result;
        }

        private static double BalancedCost(StarSystem current, StarSystem candidate, IReadOnlyList<StarSystem> route, double jumpRange)
        {
            var travel = PenalisedDistance(current, candidate, jumpRange);
            var coverage = route.Min(x => x.Coordinates.DistanceTo(candidate.Coordinates));
            return travel * 0.55 - candidate.CandidateScore * 0.25 - coverage * 0.20;
        }

        private static double SpiralKey(StarSystem origin, StarSystem candidate, double turns)
        {
            var dx = candidate.Coordinates.X - origin.Coordinates.X;
            var dy = candidate.Coordinates.Y - origin.Coordinates.Y;
            var dz = candidate.Coordinates.Z - origin.Coordinates.Z;
            var radius = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (radius < 0.001) return 0;
            var vertical = (dy / radius + 1) / 2;
            var azimuth = Math.Atan2(dz, dx);
            if (azimuth < 0) azimuth += Math.PI * 2;
            var phase = azimuth + vertical * turns * Math.PI * 2;
            return phase % (Math.PI * 2) + vertical * Math.PI * 2;
        }

        private static int Octant(StarSystem origin, StarSystem candidate)
        {
            var value = 0;
            if (candidate.Coordinates.X >= origin.Coordinates.X) value |= 1;
            if (candidate.Coordinates.Y >= origin.Coordinates.Y) value |= 2;
            if (candidate.Coordinates.Z >= origin.Coordinates.Z) value |= 4;
            return value;
        }

        public static double TotalDistance(IReadOnlyList<StarSystem> route)
        {
            double total = 0;
            for (var i = 1; i < route.Count; i++) total += route[i - 1].Coordinates.DistanceTo(route[i].Coordinates);
            return total;
        }

        private static double PenalisedDistance(StarSystem from, StarSystem to, double jumpRange)
        {
            var distance = from.Coordinates.DistanceTo(to.Coordinates);
            if (jumpRange <= 0) return distance;
            var jumps = Math.Ceiling(distance / jumpRange);
            return jumps * jumpRange + distance * 0.001;
        }

        private static void ImproveWithTwoOpt(List<StarSystem> route, int passes)
        {
            for (var pass = 0; pass < passes; pass++)
            {
                var improved = false;
                for (var i = 1; i < route.Count - 2; i++)
                for (var k = i + 1; k < route.Count - 1; k++)
                {
                    var oldDistance = route[i - 1].Coordinates.DistanceTo(route[i].Coordinates)
                                    + route[k].Coordinates.DistanceTo(route[k + 1].Coordinates);
                    var newDistance = route[i - 1].Coordinates.DistanceTo(route[k].Coordinates)
                                    + route[i].Coordinates.DistanceTo(route[k + 1].Coordinates);
                    if (newDistance + 0.0001 < oldDistance)
                    {
                        route.Reverse(i, k - i + 1);
                        improved = true;
                    }
                }
                if (!improved) break;
            }
        }
    }
}
