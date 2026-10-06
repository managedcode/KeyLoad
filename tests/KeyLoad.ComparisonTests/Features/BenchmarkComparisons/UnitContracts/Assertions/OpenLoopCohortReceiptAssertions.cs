using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopCohortReceiptAssertions
{
    private const string FailedDisposition = "failed";
    private const string FailureReason = "Benchmark failed; no measurement data is available.";
    private const string ReceiptKind = "open-loop-cohort-receipt.v1";
    private const int MeasurementCount = 792;
    private const int ProofCount = 6;
    private const string PlanFile = "open-loop-isolated-plan.v1.json";
    private const string MeasurementPrefix = "comparison-open-loop-worker-";
    private const string ProofPrefix = "comparison-open-loop-proof-";

    internal static async Task VerifyAllFailedAsync(JsonElement receipt, JsonElement plan, string inputRoot,
        CancellationToken cancellationToken)
    {
        var expected = await ReadExpectedIdsAsync(cancellationToken).ConfigureAwait(false);
        await VerifyReceiptHeaderAsync(receipt, plan, inputRoot, expected, cancellationToken).ConfigureAwait(false);
        VerifyRows(receipt.GetProperty(OpenLoopCohortReceiptFields.Cells), expected);
        await VerifyCountsAsync(receipt, expected).ConfigureAwait(false);
    }

    internal static async Task VerifyRetainedEvidenceAsync(string input, string output,
        CancellationToken cancellationToken)
    {
        var sourcePlan = await File.ReadAllBytesAsync(Path.Combine(input, PlanFile), cancellationToken)
            .ConfigureAwait(false);
        var retainedPlan = await File.ReadAllBytesAsync(Path.Combine(output, PlanFile), cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(retainedPlan.SequenceEqual(sourcePlan)).IsTrue();
        await VerifyRetainedTreeAsync(Path.Combine(input, "cells"), Path.Combine(output, "cells"),
            cancellationToken).ConfigureAwait(false);
        await VerifyRetainedTreeAsync(Path.Combine(input, "archives"), Path.Combine(output, "archives"),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyRetainedTreeAsync(string sourceRoot, string outputRoot,
        CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
        var outputFiles = Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories);
        await Assert.That(outputFiles.Length).IsEqualTo(sourceFiles.Length);
        foreach (var source in sourceFiles)
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var retained = Path.Combine(outputRoot, relative);
            var sourceBytes = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
            var retainedBytes = await File.ReadAllBytesAsync(retained, cancellationToken).ConfigureAwait(false);
            await Assert.That(retainedBytes.SequenceEqual(sourceBytes)).IsTrue();
        }
    }

    private static async Task<string[]> ReadExpectedIdsAsync(CancellationToken cancellationToken)
    {
        var options = OpenLoopPlanProcessOptionsBinding.Capture();
        var scaled = await OpenLoopPlanNodeProcess.RunAsync(options, OpenLoopPlanNodeProgram.ScaledOperation,
            input: null, outputPath: null, keepStandardInputOpen: false, ready: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Assert.That(scaled.ExitCode).IsEqualTo(0).Because(scaled.Error);
        using var scaledPlan = JsonDocument.Parse(scaled.Output);
        using var contract = await OpenLoopPlanNodeProcess.ReadContractAsync(cancellationToken).ConfigureAwait(false);
        var rows = OpenLoopPlanExpectedInventory.Build(contract.RootElement, scaledPlan.RootElement);
        return rows.Measurements.Concat(rows.Proofs).Select(cell => cell.Id).ToArray();
    }

    private static async Task VerifyReceiptHeaderAsync(JsonElement receipt, JsonElement plan, string inputRoot,
        string[] expected, CancellationToken cancellationToken)
    {
        var planBytes = await File.ReadAllBytesAsync(Path.Combine(inputRoot, PlanFile), cancellationToken)
            .ConfigureAwait(false);
        var planHash = Convert.ToHexStringLower(SHA256.HashData(planBytes));
        await Assert.That(receipt.GetProperty(OpenLoopCohortReceiptFields.SchemaVersion).GetInt32()).IsEqualTo(1);
        await Assert.That(receipt.GetProperty(OpenLoopCohortReceiptFields.Kind).GetString()).IsEqualTo(ReceiptKind);
        await Assert.That(receipt.GetProperty(OpenLoopCohortReceiptFields.PlanSha256).GetString()).IsEqualTo(planHash);
        await Assert.That(receipt.GetProperty(OpenLoopCohortReceiptFields.Qualified).GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty(OpenLoopCohortReceiptFields.Cells).GetArrayLength()).IsEqualTo(expected.Length);
        await Assert.That(receipt.GetProperty(OpenLoopCohortReceiptFields.FailedCellIds).EnumerateArray().Select(item => item.GetString())
            .SequenceEqual(expected.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(plan.GetProperty(OpenLoopCohortReceiptFields.MeasurementCells).GetArrayLength())
            .IsEqualTo(MeasurementCount);
    }

    private static void VerifyRows(JsonElement rows, string[] expected)
    {
        var index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var cell = row.GetProperty(OpenLoopCohortReceiptFields.Cell);
            var id = cell.GetProperty(OpenLoopCohortReceiptFields.Id).GetString();
            if (id != expected[index++])
            { throw new InvalidDataException("The receipt cell order changed."); }
            if (row.GetProperty(OpenLoopCohortReceiptFields.Disposition).GetString() != FailedDisposition
                || row.GetProperty(OpenLoopCohortReceiptFields.Reason).GetString() != FailureReason
                || row.GetProperty(OpenLoopCohortReceiptFields.Artifacts).GetArrayLength() != 0)
            {
                throw new InvalidDataException("A failed cell acquired success evidence.");
            }
            VerifyMetadata(row, cell);
        }
    }

    private static void VerifyMetadata(JsonElement row, JsonElement cell)
    {
        var job = row.GetProperty(OpenLoopCohortReceiptFields.Job);
        var artifact = row.GetProperty(OpenLoopCohortReceiptFields.Artifact);
        var prefix = cell.GetProperty(OpenLoopCohortReceiptFields.CancellationProof).GetBoolean() ? ProofPrefix : MeasurementPrefix;
        if (job.GetProperty(OpenLoopCohortReceiptFields.Conclusion).GetString() != "failure"
            || artifact.GetProperty(OpenLoopCohortReceiptFields.Name).GetString() != prefix + cell.GetProperty(OpenLoopCohortReceiptFields.Id).GetString()
            || artifact.GetProperty(OpenLoopCohortReceiptFields.Expired).GetBoolean())
        {
            throw new InvalidDataException("The retained failure identity changed.");
        }
    }

    private static async Task VerifyCountsAsync(JsonElement receipt, string[] expected)
    {
        var counts = receipt.GetProperty(OpenLoopCohortReceiptFields.Counts);
        await Assert.That(counts.GetProperty(OpenLoopCohortReceiptFields.PlannedMeasurements).GetInt32())
            .IsEqualTo(MeasurementCount);
        await Assert.That(counts.GetProperty(OpenLoopCohortReceiptFields.PlannedProofs).GetInt32())
            .IsEqualTo(ProofCount);
        await Assert.That(counts.GetProperty(OpenLoopCohortReceiptFields.Measured).GetInt32()).IsEqualTo(0);
        await Assert.That(counts.GetProperty(OpenLoopCohortReceiptFields.CancellationProof).GetInt32()).IsEqualTo(0);
        await Assert.That(counts.GetProperty(OpenLoopCohortReceiptFields.UnsupportedTopology).GetInt32()).IsEqualTo(0);
        await Assert.That(counts.GetProperty(OpenLoopCohortReceiptFields.Failed).GetInt32()).IsEqualTo(expected.Length);
    }
}
