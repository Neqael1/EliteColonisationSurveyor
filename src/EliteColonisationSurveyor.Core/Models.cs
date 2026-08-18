using System;

namespace EliteColonisationSurveyor.Core
{
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
        public string PrimaryStarType { get; set; }
        public double CandidateScore { get; set; }
    }

    public sealed class ShipProfile
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public double JumpRange { get; set; }
    }

    public sealed class SearchSettings
    {
        public double RadiusLy { get; set; } = 50;
        public int MaximumSystems { get; set; } = 100;
        public bool ExcludePopulated { get; set; } = true;
        public bool ExcludePermitLocked { get; set; } = true;
        public bool PreferScoopableStars { get; set; } = true;
    }
}
