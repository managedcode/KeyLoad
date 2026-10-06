using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-SCALE-015: incomplete scale accounting remains an uploaded diagnostic, never a success receipt.</summary>
internal sealed class ScaledCohortAggregateFailureTests
{
    private const string Script = "scaled-cohort-aggregate-cli.mjs";
    private const string InputArgument = "--input=";
    private const string PlanArgument = "--plan=";
    private const string ScalePlanArgument = "--scale-plan=";
    private const string CompositePlanArgument = "--composite-plan=";
    private const string ProofRootArgument = "--proof-root=";
    private const string OutputArgument = "--output=";
    private const string ScaleOutputArgument = "--scale-output=";

    [Test]
    public async Task MissingRequiredPlanWritesOnlyClosedFailureAccounting()
    {
        using var directory = new IsolatedPlanDirectory();
        var input = directory.PathFor("input");
        Directory.CreateDirectory(input);
        var scaleOutput = directory.PathFor("scale-cohort");
        var token = TestContext.Current!.Execution.CancellationToken;
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            [IsolatedAggregateNodeProcess.Module(Script), InputArgument + input,
                PlanArgument + directory.PathFor("missing-plan.json"), ScalePlanArgument + directory.PathFor("missing-scale.json"),
                "--vector-plan=" + directory.PathFor("missing-vectors.json"), CompositePlanArgument + directory.PathFor("missing-composite.json"), ProofRootArgument + directory.PathFor("proof"),
                OutputArgument + directory.PathFor("aggregate"), ScaleOutputArgument + scaleOutput], token);

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error).DoesNotContain(directory.Root);
        using var diagnostic = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(scaleOutput, "failed-accounting.json"), token));
        await Assert.That(diagnostic.RootElement.EnumerateObject().Select(property => property.Name)
            .SequenceEqual([IsolatedPlanFields.SchemaVersion, IsolatedPlanFields.Status, IsolatedPlanFields.Error], StringComparer.Ordinal)).IsTrue();
        await Assert.That(diagnostic.RootElement.GetProperty(IsolatedPlanFields.SchemaVersion).GetInt32()).IsEqualTo(1);
        await Assert.That(diagnostic.RootElement.GetProperty(IsolatedPlanFields.Status).GetString()).IsEqualTo("incomplete");
        await Assert.That(diagnostic.RootElement.GetProperty(IsolatedPlanFields.Error).GetString()).IsEqualTo("E_AGGREGATE_INPUT");
        await Assert.That(File.Exists(Path.Combine(scaleOutput, "cohort-receipt.json"))).IsFalse();
        await Assert.That(Directory.Exists(directory.PathFor("aggregate"))).IsFalse();
    }
}
