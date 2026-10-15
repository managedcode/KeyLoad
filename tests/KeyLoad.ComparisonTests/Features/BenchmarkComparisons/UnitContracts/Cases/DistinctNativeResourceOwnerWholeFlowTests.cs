using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class DistinctNativeResourceOwnerWholeFlowTests
{
    private const string InputMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const string ResourceModule = "server-resource-evidence.mjs";
    private const string ProfileProperty = "profile";
    private const string CellsProperty = "cells";
    private const string NodesProperty = "nodes";
    private const string OrdinalProperty = "duplicatedOrdinal";
    private const string NullReportsProperty = "allReportsNull";
    private const string InputsProperty = "originalInputsPreserved";
    private const string AggregateProperty = "originalAggregatePreserved";
    private const string RejectedProperty = "rejectedOutputAbsent";
    private const string RepairProperty = "exactRepair";
    private const string HundredThousand = "scaled-100k-c16";
    private const string Million = "scaled-1m-c16";
    private const int CanonicalCells = 88;
    private const int NativeNodes = 3;

    [Test]
    [Arguments(0, 1)]
    [Arguments(0, 2)]
    [Arguments(1, 1)]
    [Arguments(1, 2)]
    public async Task DuplicatePhysicalOwnerRejectsWholeScaleCellAndAggregateThenExactOriginalRepairPreservesInputsAndPriorPublication(int profile, int duplicate)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var directory = new IsolatedPlanDirectory();
            await ServerFailureObserver.ObserveAsync(() => RunAsync(directory, profile, duplicate), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunAsync(IsolatedPlanDirectory directory, int profile, int duplicate)
    {
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            [InputMode, EvaluationMode, DistinctNativeResourceOwnerProgram.Source, directory.Root,
                IsolatedAggregateNodeProcess.Module(ResourceModule), profile.ToString(System.Globalization.CultureInfo.InvariantCulture),
                duplicate.ToString(System.Globalization.CultureInfo.InvariantCulture)], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEmpty();
        using var response = JsonDocument.Parse(result.Output);
        var value = response.RootElement;
        await Assert.That(value.EnumerateObject().Count()).IsEqualTo(9);
        await Assert.That(value.GetProperty(ProfileProperty).GetString()).IsEqualTo(profile == 0 ? HundredThousand : Million);
        await Assert.That(value.GetProperty(CellsProperty).GetInt32()).IsEqualTo(CanonicalCells);
        await Assert.That(value.GetProperty(NodesProperty).GetInt32()).IsEqualTo(NativeNodes);
        await Assert.That(value.GetProperty(OrdinalProperty).GetInt32()).IsEqualTo(duplicate + 1);
        foreach (var property in new[] { NullReportsProperty, InputsProperty, AggregateProperty, RejectedProperty, RepairProperty })
        {
            await Assert.That(value.GetProperty(property).GetBoolean()).IsTrue();
        }
    }
}
