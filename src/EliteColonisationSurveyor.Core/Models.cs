using System;

namespace EliteColonisationSurveyor.Core
{
    public enum SearchPattern
    {
        Balanced,
        ShortestRoute,
        ConcentricShells,
        Spiral3D,
        OctantSweep,
        ScoreFirst,
        BoundarySurvey,
        JumpSafe
    }

    public sealed class Coordinates
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public double DistanceTo(Coordinates other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            var dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public sealed class StarSystem
    {
        public string Name { get; set; }
        public long Id { get; set; }
        public double DistanceFromCentre { get; set; }
        public Coordinates Coordinates { get; set; }
        public bool RequiresPermit { get; set; }
        public long Population { get; set; }
        public string Allegiance { get; set; }
        public string Government { get; set; }
        public string Economy { get; set; }
        public string Security { get; set; }
        public string PrimaryStarType { get; set; }
        public double CandidateScore { get; set; }
        public string ScoreBreakdown { get; set; }
        public bool BodyDataLookupSucceeded { get; set; }
        public bool BodyDataAvailable { get; set; }
        public double BodyDataCompleteness { get; set; }
        public int KnownBodyCount { get; set; }
        public int HabitableBodyCount { get; set; }
        public int TerraformableBodyCount { get; set; }
        public int LandableBodyCount { get; set; }
        public int ResourceBodyCount { get; set; }
        public int ValuableRingCount { get; set; }
        public double NearestUsefulArrivalLs { get; set; }

        public bool IsColonised => Population > 0
            || !string.IsNullOrWhiteSpace(Allegiance)
            || !string.IsNullOrWhiteSpace(Government)
            || !string.IsNullOrWhiteSpace(Economy)
            || !string.IsNullOrWhiteSpace(Security);
    }

    public sealed class ShipProfile
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public double JumpRange { get; set; }
    }

    public sealed class SearchSettings
    {
        public double RadiusLy { get; set; } = 15;
        public int MaximumSystems { get; set; } = 100;
        public bool ExcludeColonised { get; set; } = true;
        public bool ExcludePermitLocked { get; set; } = true;
        public bool PreferScoopableStars { get; set; } = true;
        public bool OnlySystemsWithoutBodyData { get; set; }
        public SearchPattern Pattern { get; set; } = SearchPattern.Balanced;
        public ScoreWeights Weights { get; set; } = new ScoreWeights();
        public double? MinimumScore { get; set; }
    }

    public sealed class ScoreWeights
    {
        public double Uncolonised { get; set; } = 60;
        public double Colonised { get; set; } = 5;
        public double NoPermitRequired { get; set; } = 20;
        public double PermitRequired { get; set; } = -100;
        public double ScoopablePrimary { get; set; } = 15;
        public double NearCentre { get; set; } = 5;
        public double BodySuitability { get; set; } = 25;
        public double ResourcePotential { get; set; } = 15;
        public double ArrivalConvenience { get; set; } = 10;
        public double StellarHazard { get; set; } = -25;
        public double DataConfidence { get; set; } = 5;
        public double UnknownDataPercent { get; set; } = 50;
    }
}
