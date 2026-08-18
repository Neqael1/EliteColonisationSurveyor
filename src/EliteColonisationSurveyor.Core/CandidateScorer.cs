using System;
using System.Collections.Generic;
using System.Linq;

namespace EliteColonisationSurveyor.Core
{
    public sealed class CandidateScorer
    {
        private static readonly HashSet<string> Scoopable = new HashSet<string>(
            new[] { "O", "B", "A", "F", "G", "K", "M" }, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<StarSystem> Rank(IEnumerable<StarSystem> systems, SearchSettings settings)
        {
            if (systems == null) throw new ArgumentNullException(nameof(systems));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            return systems
                .Where(s => s != null && s.Coordinates != null)
                .Where(s => s.DistanceFromCentre > 0 && s.DistanceFromCentre <= settings.RadiusLy)
                .Where(s => !settings.ExcludeColonised || !s.IsColonised)
                .Where(s => !settings.ExcludePermitLocked || !s.RequiresPermit)
                .Select(s => { s.CandidateScore = Score(s, settings); return s; })
                .OrderByDescending(s => s.CandidateScore)
                .ThenBy(s => s.DistanceFromCentre)
                .Take(Math.Max(1, settings.MaximumSystems))
                .ToList();
        }

        private static double Score(StarSystem system, SearchSettings settings)
        {
            double score = system.IsColonised ? 5 : 60;
            score += system.RequiresPermit ? -100 : 20;
            if (settings.PreferScoopableStars && IsScoopable(system.PrimaryStarType)) score += 15;
            score += Math.Max(0, 5 - system.DistanceFromCentre / Math.Max(1, settings.RadiusLy) * 5);
            return Math.Round(score, 2);
        }

        private static bool IsScoopable(string starType)
        {
            if (string.IsNullOrWhiteSpace(starType)) return false;
            var token = starType.Trim().Split(' ')[0];
            return Scoopable.Contains(token) || Scoopable.Any(x => token.StartsWith(x, StringComparison.OrdinalIgnoreCase));
        }
    }
}
