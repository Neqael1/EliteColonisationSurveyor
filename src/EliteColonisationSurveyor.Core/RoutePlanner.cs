using System;
using System.Collections.Generic;
using System.Linq;

namespace EliteColonisationSurveyor.Core
{
    public sealed class RoutePlanner
    {
        public IReadOnlyList<StarSystem> Plan(StarSystem origin, IEnumerable<StarSystem> candidates, double jumpRange)
        {
            if (origin == null || origin.Coordinates == null) throw new ArgumentNullException(nameof(origin));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var remaining = candidates.Where(x => x != null && x.Coordinates != null && x.Name != origin.Name).ToList();
            var route = new List<StarSystem> { origin };
            var current = origin;

            while (remaining.Count > 0)
            {
                var next = remaining
                    .OrderBy(x => PenalisedDistance(current, x, jumpRange))
                    .ThenByDescending(x => x.CandidateScore)
                    .First();
                route.Add(next);
                remaining.Remove(next);
                current = next;
            }

            ImproveWithTwoOpt(route, 8);
            return route;
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
