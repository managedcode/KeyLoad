using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanExpectedInventory
{
    internal const string MeasurementFamily = "open-loop";
    internal const string ProofFamily = "open-loop-proof";
    internal const string MeasurementSuffix = "-openloop-r";
    internal const string ProofSuffix = "-openloop-proof-r";
    internal const int ExpectedMeasurementCount = 528;
    internal const int ExpectedProofCount = 6;
    internal const int ExpectedPerTarget = 48;
    internal const int ExpectedUnsupportedCount = 72;
    internal const int MaximumIdLength = 120;
    internal const int ExpectedTargetCount = 11;
    internal const int ExpectedNodeCount = 2;
    internal const int ExpectedScenarioCount = 4;
    internal const int ExpectedProfileCount = 2;
    internal const int ExpectedRateCount = 3;
    internal const int InvalidOfferedRate = 999;
    internal const int SchemaVersion = 1;
    internal const string SchemaVersionProperty = "schemaVersion";
    internal const string KindProperty = "kind";
    internal const string MeasurementCellsProperty = "measurementCells";
    internal const string ProofCellsProperty = "cancellationProofCells";
    internal const string PlanKind = "open-loop-isolated-plan.v1";
    internal const string OfferedRateProperty = "offeredRatePerSecond";
    internal const string CancellationProofProperty = "cancellationProof";
    internal const string FamilyProperty = "family";
    internal const int KeyLoadNodeCount = 3;
    internal const string KeyLoadTarget = "KeyLoad";
    internal const string PointReadScenario = "PointRead";
    internal const string CellsProperty = "cells";
    internal const string TargetsProperty = "targets";
    internal const string NodesProperty = "nodeCounts";
    internal const string ScenariosProperty = "crudScenarios";
    internal const string UnsupportedProperty = "unsupportedTopologies";
    internal const string TargetProperty = "target";
    internal const string NodeProperty = "nodeCount";
    internal const string ScenarioProperty = "scenario";
    internal const string ProfileProperty = "profile";
    internal const string IdProperty = "id";
    internal static readonly int[] Rates = [250, 1_000, 4_000];
    internal static readonly string[] CellFields =
    ["id", "target", "nodeCount", "scenario", "profile", "family", "offeredRatePerSecond", "cancellationProof"];
    internal static string UnsupportedKey(string target, int nodeCount)
        => Identity(target, nodeCount, "*", "*");

    internal static (IReadOnlyList<OpenLoopPlanExpectedCell> Measurements,
        IReadOnlyList<OpenLoopPlanExpectedCell> Proofs, HashSet<string> Unsupported) Build(
        JsonElement contract, JsonElement scaledPlans)
    {
        var scaledCells = IndexScaledCells(scaledPlans);
        var unsupported = ReadUnsupported(contract);
        var measurements = BuildMeasurements(contract, scaledCells);
        var proofs = BuildProofs(scaledCells);
        return (measurements, proofs, unsupported);
    }

    private static Dictionary<string, JsonElement> IndexScaledCells(JsonElement scaledPlans)
    {
        var index = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var plan in scaledPlans.EnumerateArray())
        {
            foreach (var cell in plan.GetProperty(CellsProperty).EnumerateArray())
            {
                var key = Identity(cell.GetProperty(TargetProperty).GetString()!, cell.GetProperty(NodeProperty).GetInt32(),
                    cell.GetProperty(ScenarioProperty).GetString()!, cell.GetProperty(ProfileProperty).GetString()!);
                if (!index.TryAdd(key, cell))
                {
                    throw new InvalidOperationException("The existing scaled plan contains duplicate identities.");
                }
            }
        }
        return index;
    }

    private static HashSet<string> ReadUnsupported(JsonElement contract)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var topology in contract.GetProperty(UnsupportedProperty).EnumerateArray())
        {
            var target = topology.GetProperty(TargetProperty).GetString()!;
            foreach (var nodeCount in topology.GetProperty(IsolatedPlanFields.NodeCounts).EnumerateArray())
            {
                result.Add(Identity(target, nodeCount.GetInt32(), "*", "*"));
            }
        }
        return result;
    }

    private static List<OpenLoopPlanExpectedCell> BuildMeasurements(JsonElement contract,
        IReadOnlyDictionary<string, JsonElement> scaled)
    {
        var targets = contract.GetProperty(TargetsProperty).EnumerateArray().Select(item => item.GetString()!).ToArray();
        var nodes = contract.GetProperty(NodesProperty).EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var scenarios = contract.GetProperty(ScenariosProperty).EnumerateArray().Select(item => item.GetString()!).ToArray();
        var profiles = ReadProfiles(scaled);
        if (targets.Length != ExpectedTargetCount || nodes.Length != ExpectedNodeCount
            || scenarios.Length != ExpectedScenarioCount || profiles.Length != ExpectedProfileCount
            || Rates.Length != ExpectedRateCount)
        {
            throw new InvalidOperationException("The canonical open-loop dimensions do not match the frozen inventory.");
        }
        var result = new List<OpenLoopPlanExpectedCell>(ExpectedMeasurementCount);
        for (var index = 0; index < ExpectedMeasurementCount; index++)
        {
            var rateIndex = index % ExpectedRateCount;
            var profileIndex = index / ExpectedRateCount % ExpectedProfileCount;
            var scenarioIndex = index / (ExpectedRateCount * ExpectedProfileCount) % ExpectedScenarioCount;
            var nodeIndex = index / (ExpectedRateCount * ExpectedProfileCount * ExpectedScenarioCount) % ExpectedNodeCount;
            var targetIndex = index / (ExpectedRateCount * ExpectedProfileCount * ExpectedScenarioCount * ExpectedNodeCount);
            result.Add(Create(scaled, targets[targetIndex], nodes[nodeIndex], scenarios[scenarioIndex], profiles[profileIndex],
                Rates[rateIndex], MeasurementFamily, MeasurementSuffix, cancellationProof: false));
        }
        return result;
    }

    private static string[] ReadProfiles(IReadOnlyDictionary<string, JsonElement> scaled)
    {
        var profiles = scaled.Values.Select(cell => cell.GetProperty(ProfileProperty).GetString()!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return profiles;
    }

    private static List<OpenLoopPlanExpectedCell> BuildProofs(IReadOnlyDictionary<string, JsonElement> scaled)
    {
        var result = new List<OpenLoopPlanExpectedCell>(ExpectedProofCount);
        foreach (var profile in ReadProfiles(scaled))
        {
            foreach (var rate in Rates)
            {
                result.Add(Create(scaled, KeyLoadTarget, KeyLoadNodeCount, PointReadScenario, profile, rate,
                    ProofFamily, ProofSuffix, cancellationProof: true));
            }
        }
        return result;
    }

    private static OpenLoopPlanExpectedCell Create(IReadOnlyDictionary<string, JsonElement> scaled,
        string target, int nodeCount, string scenario, string profile, int rate, string family,
        string suffix, bool cancellationProof)
    {
        var key = Identity(target, nodeCount, scenario, profile);
        if (!scaled.TryGetValue(key, out var source))
        {
            throw new InvalidOperationException("The canonical scaled plan is missing a required identity.");
        }
        var id = source.GetProperty(IdProperty).GetString() + suffix + rate;
        if (id.Length > MaximumIdLength)
        {
            throw new InvalidOperationException("The canonical open-loop identifier exceeds its bound.");
        }
        return new OpenLoopPlanExpectedCell(id, target, nodeCount, scenario, profile, family, rate, cancellationProof);
    }

    private static string Identity(string target, int nodeCount, string scenario, string profile)
        => string.Join('\u001f', target, nodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture), scenario, profile);
}
