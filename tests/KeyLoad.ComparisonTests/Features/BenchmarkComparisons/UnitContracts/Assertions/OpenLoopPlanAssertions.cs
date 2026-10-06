using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanAssertions
{
    internal static async Task VerifyCanonicalAsync(JsonElement plan, JsonElement contract, JsonElement scaledPlans)
    {
        await Assert.That(PropertyNames(plan)).IsEquivalentTo(
            new[] { OpenLoopPlanExpectedInventory.SchemaVersionProperty, OpenLoopPlanExpectedInventory.KindProperty,
                OpenLoopPlanExpectedInventory.MeasurementCellsProperty, OpenLoopPlanExpectedInventory.ProofCellsProperty },
            CollectionOrdering.Matching);
        await Assert.That(plan.GetProperty(OpenLoopPlanExpectedInventory.SchemaVersionProperty).GetInt32())
            .IsEqualTo(OpenLoopPlanExpectedInventory.SchemaVersion);
        await Assert.That(plan.GetProperty(OpenLoopPlanExpectedInventory.KindProperty).GetString())
            .IsEqualTo(OpenLoopPlanExpectedInventory.PlanKind);
        var expected = OpenLoopPlanExpectedInventory.Build(contract, scaledPlans);
        var actualMeasurements = ReadCells(plan.GetProperty(OpenLoopPlanExpectedInventory.MeasurementCellsProperty));
        var actualProofs = ReadCells(plan.GetProperty(OpenLoopPlanExpectedInventory.ProofCellsProperty));
        await Assert.That(actualMeasurements).IsEquivalentTo(expected.Measurements, CollectionOrdering.Matching);
        await Assert.That(actualProofs).IsEquivalentTo(expected.Proofs, CollectionOrdering.Matching);
        await VerifyIdentityPartitionAsync(actualMeasurements, actualProofs, expected.Unsupported,
            contract.GetProperty(IsolatedPlanFields.Targets));
    }

    private static async Task VerifyIdentityPartitionAsync(OpenLoopPlanExpectedCell[] measurements,
        OpenLoopPlanExpectedCell[] proofs, HashSet<string> unsupported, JsonElement targets)
    {
        await Assert.That(measurements.Length).IsEqualTo(OpenLoopPlanExpectedInventory.ExpectedMeasurementCount);
        await Assert.That(proofs.Length).IsEqualTo(OpenLoopPlanExpectedInventory.ExpectedProofCount);
        var measurementIds = measurements.Select(cell => cell.Id).ToHashSet(StringComparer.Ordinal);
        await Assert.That(measurementIds.Count).IsEqualTo(measurements.Length);
        await Assert.That(proofs.Select(cell => cell.Id).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(proofs.Length);
        await Assert.That(proofs.All(cell => !measurementIds.Contains(cell.Id))).IsTrue();
        var unsupportedCount = measurements.Count(cell => unsupported.Contains(
            OpenLoopPlanExpectedInventory.UnsupportedKey(cell.Target, cell.NodeCount)));
        await Assert.That(unsupportedCount).IsEqualTo(OpenLoopPlanExpectedInventory.ExpectedUnsupportedCount);
        await VerifyTargetCountsAsync(measurements, targets);
        await Assert.That(proofs.All(cell => cell.CancellationProof && cell.Family == OpenLoopPlanExpectedInventory.ProofFamily
            && cell.Target == "KeyLoad" && cell.NodeCount == 3 && cell.Scenario == "PointRead")).IsTrue();
    }

    private static async Task VerifyTargetCountsAsync(OpenLoopPlanExpectedCell[] measurements, JsonElement targets)
    {
        foreach (var target in targets.EnumerateArray())
        {
            var name = target.GetString();
            await Assert.That(measurements.Count(cell => cell.Target == name))
                .IsEqualTo(OpenLoopPlanExpectedInventory.ExpectedPerTarget);
        }
    }

    private static OpenLoopPlanExpectedCell[] ReadCells(JsonElement values)
        => values.EnumerateArray().Select(ReadCell).ToArray();

    private static OpenLoopPlanExpectedCell ReadCell(JsonElement value)
    {
        if (!PropertyNames(value).SequenceEqual(OpenLoopPlanExpectedInventory.CellFields, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("An open-loop plan cell has unexpected fields or field order.");
        }
        return new OpenLoopPlanExpectedCell(value.GetProperty(IsolatedPlanFields.Id).GetString()!, value.GetProperty(IsolatedPlanFields.Target).GetString()!,
            value.GetProperty(IsolatedPlanFields.NodeCount).GetInt32(), value.GetProperty(IsolatedPlanFields.Scenario).GetString()!,
            value.GetProperty(IsolatedPlanFields.Profile).GetString()!, value.GetProperty(OpenLoopPlanExpectedInventory.FamilyProperty).GetString()!,
            value.GetProperty(OpenLoopPlanExpectedInventory.OfferedRateProperty).GetInt32(),
            value.GetProperty(OpenLoopPlanExpectedInventory.CancellationProofProperty).GetBoolean());
    }

    private static string[] PropertyNames(JsonElement value)
        => value.EnumerateObject().Select(property => property.Name).ToArray();
}
