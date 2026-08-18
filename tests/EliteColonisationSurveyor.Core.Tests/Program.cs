using System;
using System.Collections.Generic;
using EliteColonisationSurveyor.Core;

static StarSystem Star(string name, double x, double y = 0, long population = 0, bool permit = false) =>
    new StarSystem { Name = name, Coordinates = new Coordinates { X = x, Y = y }, DistanceFromCentre = Math.Sqrt(x*x+y*y), Population = population, RequiresPermit = permit };

void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

var scorer = new CandidateScorer();
var ranked = scorer.Rank(new[] { Star("good", 5), Star("populated", 6, population: 100), Star("permit", 7, permit: true), Star("outside", 60) }, new SearchSettings { RadiusLy = 50 });
Assert(ranked.Count == 1 && ranked[0].Name == "good", "Candidate filters failed");

var inhabitedMetadata = Star("metadata-colony", 8);
inhabitedMetadata.Economy = "Industrial";
ranked = scorer.Rank(new[] { Star("uninhabited", 5), inhabitedMetadata }, new SearchSettings { RadiusLy = 50 });
Assert(ranked.Count == 1 && ranked[0].Name == "uninhabited", "Colonised-system metadata filter failed");

var scoopable = Star("scoopable", 20); scoopable.PrimaryStarType = "G (White-Yellow) Star";
var nonScoopable = Star("non-scoopable", 5); nonScoopable.PrimaryStarType = "T Tauri Star";
ranked = scorer.Rank(new[] { nonScoopable, scoopable }, new SearchSettings {
    RadiusLy = 50, Weights = new ScoreWeights { Uncolonised = 0, NoPermitRequired = 0, ScoopablePrimary = 100, NearCentre = 0 }
});
Assert(ranked[0].Name == "scoopable" && ranked[0].CandidateScore == 100, "Custom scoopable coefficient was not applied");

ranked = scorer.Rank(new[] { Star("near", 5), Star("farther", 40) }, new SearchSettings {
    RadiusLy = 50, Weights = new ScoreWeights { Uncolonised = 0, NoPermitRequired = 0, ScoopablePrimary = 0, NearCentre = 100 }
});
Assert(ranked[0].Name == "near" && ranked[0].CandidateScore > ranked[1].CandidateScore, "Custom distance coefficient was not applied");

var detailed = Star("detailed", 10);
detailed.BodyDataAvailable = true; detailed.BodyDataCompleteness = 1; detailed.HabitableBodyCount = 2; detailed.TerraformableBodyCount = 2;
detailed.ResourceBodyCount = 3; detailed.ValuableRingCount = 1; detailed.NearestUsefulArrivalLs = 500;
ranked = scorer.Rank(new[] { Star("basic", 10), detailed }, new SearchSettings {
    RadiusLy = 50, MinimumScore = 30, Weights = new ScoreWeights {
        Uncolonised = 0, NoPermitRequired = 0, ScoopablePrimary = 0, NearCentre = 0,
        BodySuitability = 40, ResourcePotential = 20, ArrivalConvenience = 10, DataConfidence = 5
    }
});
Assert(ranked.Count == 1 && ranked[0].Name == "detailed", "Detailed scoring or minimum-score cutoff failed");
Assert(ranked[0].ScoreBreakdown.Contains("body suitability") && ranked[0].ScoreBreakdown.Contains("resources"), "Score breakdown omitted detailed factors");

var hazard = Star("hazard", 10); hazard.PrimaryStarType = "Neutron Star";
ranked = scorer.Rank(new[] { hazard }, new SearchSettings { RadiusLy = 50, Weights = new ScoreWeights { StellarHazard = -50 } });
Assert(ranked[0].ScoreBreakdown.Contains("stellar hazard"), "Stellar hazard penalty was not applied");

var origin = Star("origin", 0);
var candidates = new List<StarSystem> { Star("east", 10), Star("north", 0, 10), Star("far", 20, 10) };
var route = new RoutePlanner().Plan(origin, candidates, 30);
Assert(route.Count == 4 && route[0].Name == "origin", "Route must retain origin and every candidate");
Assert(RoutePlanner.TotalDistance(route) < 40, "Route optimisation produced an unexpectedly long path");

foreach (var pattern in new[] { SearchPattern.Balanced, SearchPattern.ShortestRoute, SearchPattern.ConcentricShells, SearchPattern.Spiral3D, SearchPattern.OctantSweep, SearchPattern.ScoreFirst, SearchPattern.BoundarySurvey })
{
    route = new RoutePlanner().Plan(origin, candidates, 30, pattern);
    Assert(route.Count == candidates.Count + 1 && route[0].Name == "origin", pattern + " lost a candidate or origin");
    for (var i = 1; i < route.Count; i++)
        Assert(route[i - 1].Coordinates.DistanceTo(route[i].Coordinates) <= 30.001, pattern + " exceeded the ship jump range");
}

candidates[0].CandidateScore = 90;
candidates[1].CandidateScore = 20;
route = new RoutePlanner().Plan(origin, candidates, 30, SearchPattern.ScoreFirst);
Assert(route[1].Name == "east", "Score-first pattern did not prioritise the highest score");

route = new RoutePlanner().Plan(origin, candidates, 11, SearchPattern.JumpSafe);
for (var i = 1; i < route.Count; i++)
    Assert(route[i - 1].Coordinates.DistanceTo(route[i].Coordinates) <= 11.001, "Jump-safe pattern produced an unsafe leg");

Console.WriteLine("All core tests passed.");
