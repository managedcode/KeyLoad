using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopCohortDeliveryTests
{
    private const string ReceiptFile = "open-loop-cohort-receipt.v1.json";
    private const string ReceiptFailure = "{\"error\":\"Open-loop cohort evidence is invalid.\"}\n";

    [Test]
    public async Task AcScale022CompleteFailedCohortIsRetainedAndBadIntakeCanRecover()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var evidence = new OpenLoopCohortStageEvidence(OpenLoopPlanProcessOptionsBinding.Capture());
        await OpenLoopPlanTestLifetime.RunAsync(async (planPath, token) =>
        {
            await VerifyFailedCohortFlowAsync(evidence, Path.GetDirectoryName(planPath)!, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyFailedCohortFlowAsync(OpenLoopCohortStageEvidence evidence,
        string root, CancellationToken cancellationToken)
    {
        var input = Path.Combine(root, "cohort-input");
        var output = Path.Combine(root, "cohort-output");
        Directory.CreateDirectory(input);
        var seed = await OpenLoopCohortNodeProcess.SeedFailedCohortAsync(evidence, input, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(seed.ExitCode).IsEqualTo(0).Because(seed.Error);
        await Assert.That(seed.Output).IsEqualTo("{\"seeded\":798}\n");
        var intakePath = Path.Combine(input, "open-loop-cohort-intake.v1.json");
        var originalIntake = await File.ReadAllBytesAsync(intakePath, cancellationToken).ConfigureAwait(false);
        await RejectCorruptIntakeAsync(evidence, input, output, intakePath, originalIntake, cancellationToken)
            .ConfigureAwait(false);
        await AggregateRecoveredCohortAsync(evidence, input, output, intakePath, originalIntake, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RejectCorruptIntakeAsync(OpenLoopCohortStageEvidence evidence,
        string input, string output, string intakePath, byte[] originalIntake, CancellationToken cancellationToken)
    {
        var altered = JsonNode.Parse(originalIntake)!.AsObject();
        altered[OpenLoopCohortReceiptFields.Cells]![0]![OpenLoopCohortReceiptFields.Job]![OpenLoopCohortReceiptFields.Id] = 1;
        var corrupted = JsonSerializer.SerializeToUtf8Bytes(altered);
        await File.WriteAllBytesAsync(intakePath, corrupted, cancellationToken)
            .ConfigureAwait(false);
        var rejected = await OpenLoopCohortNodeProcess.AggregateAsync(evidence, input, output, OpenLoopCohortStage.RejectCorruptIntake, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(rejected.ExitCode).IsEqualTo(1);
        await Assert.That(rejected.Output).IsEqualTo(string.Empty);
        await Assert.That(rejected.Error).IsEqualTo(ReceiptFailure);
        await Assert.That(Directory.Exists(output)).IsFalse();
        var retainedCorrupted = await File.ReadAllBytesAsync(intakePath, cancellationToken).ConfigureAwait(false);
        await Assert.That(retainedCorrupted.SequenceEqual(corrupted)).IsTrue();
    }

    private static async Task AggregateRecoveredCohortAsync(OpenLoopCohortStageEvidence evidence,
        string input, string output, string intakePath, byte[] originalIntake, CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(intakePath, originalIntake, cancellationToken).ConfigureAwait(false);
        await RejectExtraArchiveAsync(evidence, input, output, cancellationToken).ConfigureAwait(false);
        var accepted = await OpenLoopCohortNodeProcess.AggregateAsync(evidence, input, output, OpenLoopCohortStage.AggregateRecoveredCohort, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(accepted.ExitCode).IsEqualTo(0).Because(accepted.Error);
        await Assert.That(accepted.Error).IsEqualTo(string.Empty);
        await Assert.That(accepted.Output).IsEqualTo("{\"schemaVersion\":1,\"qualified\":false,\"cells\":798}\n");
        await VerifyReceiptAsync(Path.Combine(output, ReceiptFile), input, cancellationToken).ConfigureAwait(false);
        await OpenLoopCohortReceiptAssertions.VerifyRetainedEvidenceAsync(input, output, cancellationToken)
            .ConfigureAwait(false);
        await RejectCreateOnlyReuseAsync(evidence, input, output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RejectExtraArchiveAsync(OpenLoopCohortStageEvidence evidence,
        string input, string output, CancellationToken cancellationToken)
    {
        var path = Path.Combine(input, "archives", "unplanned.zip");
        var bytes = "private-unplanned-archive-canary"u8.ToArray();
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        var rejected = await OpenLoopCohortNodeProcess.AggregateAsync(evidence, input, output, OpenLoopCohortStage.RejectExtraArchive, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(rejected.ExitCode).IsEqualTo(1);
        await Assert.That(rejected.Output).IsEmpty();
        await Assert.That(rejected.Error).IsEqualTo(ReceiptFailure);
        await Assert.That(Directory.Exists(output)).IsFalse();
        var retained = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        await Assert.That(retained.SequenceEqual(bytes)).IsTrue();
        File.Delete(path);
    }

    private static async Task VerifyReceiptAsync(string receiptPath, string input, CancellationToken cancellationToken)
    {
        using var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(receiptPath, cancellationToken).ConfigureAwait(false));
        using var plan = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(input, "open-loop-isolated-plan.v1.json"), cancellationToken).ConfigureAwait(false));
        await OpenLoopCohortReceiptAssertions.VerifyAllFailedAsync(receipt.RootElement, plan.RootElement, input,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RejectCreateOnlyReuseAsync(OpenLoopCohortStageEvidence evidence,
        string input, string output, CancellationToken cancellationToken)
    {
        var path = Path.Combine(output, ReceiptFile);
        var original = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var rejected = await OpenLoopCohortNodeProcess.AggregateAsync(evidence, input, output, OpenLoopCohortStage.RejectCreateOnlyReuse, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(rejected.ExitCode).IsEqualTo(1);
        await Assert.That(rejected.Error).IsEqualTo(ReceiptFailure);
        var retained = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        await Assert.That(retained.SequenceEqual(original)).IsTrue();
    }
}
