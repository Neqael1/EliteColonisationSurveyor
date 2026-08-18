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
                .Where(s => !settings.MinimumScore.HasValue || s.CandidateScore >= settings.MinimumScore.Value)
                .OrderByDescending(s => s.CandidateScore)
                .ThenBy(s => s.DistanceFromCentre)
                .Take(Math.Max(1, settings.MaximumSystems))
                .ToList();
        }

        private static double Score(StarSystem system, SearchSettings settings)
        {
            var weights = settings.Weights ?? new ScoreWeights();
            var parts = new List<string>();
            double score = Add(parts, system.IsColonised ? weights.Colonised : weights.Uncolonised, system.IsColonised ? "colonised" : "uncolonised");
            score += Add(parts, system.RequiresPermit ? weights.PermitRequired : weights.NoPermitRequired, system.RequiresPermit ? "permit" : "no permit");
            if (settings.PreferScoopableStars && IsScoopable(system.PrimaryStarType)) score += Add(parts, weights.ScoopablePrimary, "scoopable");
            score += Add(parts, weights.NearCentre * Math.Max(0, 1 - system.DistanceFromCentre / Math.Max(1, settings.RadiusLy)), "centre distance");
            if (IsHazardous(system.PrimaryStarType)) score += Add(parts, weights.StellarHazard, "stellar hazard");
            if (system.BodyDataAvailable)
            {
                var suitability = Math.Min(1, (system.HabitableBodyCount * 2 + system.TerraformableBodyCount + system.LandableBodyCount * 0.25) / 8.0);
                var resources = Math.Min(1, (system.ResourceBodyCount + system.ValuableRingCount * 2) / 8.0);
                var arrival = system.NearestUsefulArrivalLs > 0 ? Math.Max(0, 1 - system.NearestUsefulArrivalLs / 10000.0) : 0;
                score += Add(parts, weights.BodySuitability * suitability, "body suitability");
                score += Add(parts, weights.ResourcePotential * resources, "resources");
                score += Add(parts, weights.ArrivalConvenience * arrival, "arrival");
                score += Add(parts, weights.DataConfidence * Math.Max(0, Math.Min(1, system.BodyDataCompleteness)), "data confidence");
            }
            else parts.Add("body data unknown");
            system.ScoreBreakdown = string.Join("; ", parts);
            return Math.Round(score, 2);
        }

        private static double Add(List<string> parts, double value, string name)
        {
            if (Math.Abs(value) >= 0.005) parts.Add((value >= 0 ? "+" : "") + value.ToString("0.0") + " " + name);
            return value;
        }

        private static bool IsHazardous(string starType)
        {
            if (string.IsNullOrWhiteSpace(starType)) return false;
            var value = starType.ToLowerInvariant();
            return value.Contains("neutron") || value.Contains("white dwarf") || value.Contains("black hole");
        }

        private static bool IsScoopable(string starType)
        {
            if (string.IsNullOrWhiteSpace(starType)) return false;
            var token = starType.Trim().Split(' ')[0];
            return Scoopable.Contains(token) || Scoopable.Any(x => token.StartsWith(x, StringComparison.OrdinalIgnoreCase));
        }
    }
}
