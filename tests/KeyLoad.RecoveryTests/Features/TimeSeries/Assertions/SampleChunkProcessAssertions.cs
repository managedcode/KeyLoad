using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class SampleChunkProcessAssertions
{
    private const long InitialSequence = 2;
    private const long CorrectedSequence = 3;
    private const int FirstSequence = 1;
    private const long OpenGeneration = 0;

    internal static async Task VerifyAsync(string root, string mode, bool merge, CancellationToken token)
    {
        if (mode is SampleChunkCrashContract.FaultSeal or SampleChunkCrashContract.FaultMerge) { return; }
        var prepared = await ReadAsync(root, SampleChunkCrashContract.PreparedFile, token);
        if (mode is SampleChunkCrashContract.PrepareSeal or SampleChunkCrashContract.PrepareMerge)
        {
            await LiteralAsync(prepared, merge, merge ? SampleChunkCrashContract.CorrectedRevision
                : SampleChunkCrashContract.AppendedRevision, merge ? SampleChunkCrashContract.SealedGeneration : OpenGeneration);
            return;
        }
        if (mode is SampleChunkCrashContract.RecoverSeal or SampleChunkCrashContract.RecoverMerge)
        {
            var recovered = await ReadAsync(root, SampleChunkCrashContract.RecoveredFile, token);
            await LiteralAsync(recovered, merge, merge ? SampleChunkCrashContract.MergedRevision
                : SampleChunkCrashContract.SealedRevision, merge ? SampleChunkCrashContract.MergedGeneration
                : SampleChunkCrashContract.SealedGeneration);
            await Assert.That(recovered.RawImage).IsEqualTo(prepared.RawImage);
            await Assert.That(recovered.OriginalAcknowledgedReceipt).IsEqualTo(prepared.OriginalAcknowledgedReceipt);
            await OriginalOutcomeAsync(recovered);
            var healthy = await ReadAsync(root, SampleChunkCrashContract.HealthyFile, token);
            await LiteralAsync(healthy, true, SampleChunkCrashContract.MergedRevision, SampleChunkCrashContract.MergedGeneration);
            await Assert.That(healthy.OriginalAcknowledgedReceipt).IsEqualTo(prepared.OriginalAcknowledgedReceipt);
            await Assert.That(healthy.OriginalInflightResult).IsEqualTo(recovered.OriginalInflightResult);
            return;
        }
        var final = await ReadAsync(root, SampleChunkCrashContract.FinalFile, token);
        var originalHealthy = await ReadAsync(root, SampleChunkCrashContract.HealthyFile, token);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(final)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(originalHealthy)));
        await LiteralAsync(final, true, SampleChunkCrashContract.MergedRevision, SampleChunkCrashContract.MergedGeneration);
    }

    private static async Task LiteralAsync(SampleChunkCrashSnapshot snapshot, bool merge, long revision, long generation)
    {
        SampleRecord[] rows = merge
            ? [new(SampleChunkCrashContract.Series, SampleChunkCrashContract.First, FirstSequence, SampleChunkCrashContract.Tags),
               new(SampleChunkCrashContract.Series, SampleChunkCrashContract.Equal, InitialSequence, SampleChunkCrashContract.Tags),
               new(SampleChunkCrashContract.Series, SampleChunkCrashContract.Late, CorrectedSequence, SampleChunkCrashContract.Tags)]
            : [new(SampleChunkCrashContract.Series, SampleChunkCrashContract.First, FirstSequence, SampleChunkCrashContract.Tags),
               new(SampleChunkCrashContract.Series, SampleChunkCrashContract.Equal, InitialSequence, SampleChunkCrashContract.Tags)];
        var expected = new SampleChunkWindowResult(SampleChunkCrashContract.WindowId,
            SampleChunkCrashContract.From, SampleChunkCrashContract.Until, generation, revision,
            merge ? CorrectedSequence : InitialSequence, null, [.. rows], snapshot.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(snapshot.Window)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(snapshot.RawRows)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(rows)));
    }

    private static async Task OriginalOutcomeAsync(SampleChunkCrashSnapshot snapshot)
    {
        await Assert.That(snapshot.OriginalInflightResult).IsNotNull();
        var outcome = NativeSerialization.Deserialize<OperationResult>(Convert.FromHexString(snapshot.OriginalInflightResult!));
        var receipt = outcome.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(SampleChunkCrashContract.InflightId);
        await Assert.That(receipt.Token.Position).IsEqualTo(snapshot.Position);
    }

    private static async Task<SampleChunkCrashSnapshot> ReadAsync(string root, string name, CancellationToken token)
    {
        var path = Path.Combine(root, name);
        var bytes = await SampleChunkSnapshotFile.ReadAsync(path,
            RecoveryExecutionOptions.DatabaseLimits().Value.MaxBatchBytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SampleChunkCrashSnapshot>(bytes, JsonDefaults.Options)
            ?? throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
    }
}
