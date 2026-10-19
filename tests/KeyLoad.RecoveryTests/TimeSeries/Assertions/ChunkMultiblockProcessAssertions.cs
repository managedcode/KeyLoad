using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class ChunkMultiblockProcessAssertions
{
    internal static async Task VerifyAsync(string root, string mode, bool merge, CancellationToken token)
    {
        if (ChunkMultiblockCrashProtocol.IsFault(mode))
        {
            return;
        }
        var prepared = await ReadAsync(root, SampleChunkCrashContract.PreparedFile, token);
        await ChunkMultiblockProcessRows.RequireAsync(prepared, merge, prepared: true, healthy: false);
        await Assert.That(prepared.OriginalInflightResult).IsNull();
        if (!merge)
        {
            await ChunkMultiblockProcessRaw.RequireAsync(prepared, prepared: true, healthy: false);
            await ReceiptAsync(root, prepared.OriginalAdditionalResult, prepared, ChunkMultiblockCrashData.AdditionalId,
                "appendSamples", ChunkMultiblockCrashProtocol.PreparedAutomaticSealSequence);
        }
        if (ChunkMultiblockCrashProtocol.IsPrepare(mode))
        {
            return;
        }
        var recovered = await ReadAsync(root, SampleChunkCrashContract.RecoveredFile, token);
        await ChunkMultiblockProcessRows.RequireAsync(recovered, merge, prepared: false, healthy: false);
        await Assert.That(recovered.Position).IsEqualTo(checked(prepared.Position + SampleChunkCrashContract.PositionStep));
        if (merge)
        {
            await Assert.That(recovered.RawImage).IsEqualTo(prepared.RawImage);
        }
        else
        {
            await ChunkMultiblockProcessRaw.RequireAsync(recovered, prepared: false, healthy: false);
        }
        await Assert.That(recovered.OriginalAcknowledgedReceipt).IsEqualTo(prepared.OriginalAcknowledgedReceipt);
        await Assert.That(recovered.OriginalAdditionalResult).IsEqualTo(prepared.OriginalAdditionalResult);
        await Assert.That(recovered.OriginalInflightResult).IsNotNull();
        await ReceiptAsync(root, recovered.OriginalInflightResult!, prepared, SampleChunkCrashContract.InflightId,
            merge ? "mergeSampleChunkWindow" : "appendSamples",
            merge ? ChunkMultiblockCrashProtocol.RecoveredMergeRevision : ChunkMultiblockCrashProtocol.PreparedSequence);
        var healthy = await ReadAsync(root, SampleChunkCrashContract.HealthyFile, token);
        await ChunkMultiblockProcessRows.RequireAsync(healthy, merge, prepared: false, healthy: true);
        await Assert.That(healthy.Position).IsEqualTo(checked(recovered.Position + ChunkMultiblockCrashProtocol.HealthyCommits));
        await Assert.That(healthy.OriginalAcknowledgedReceipt).IsEqualTo(prepared.OriginalAcknowledgedReceipt);
        await Assert.That(healthy.OriginalAdditionalResult).IsEqualTo(prepared.OriginalAdditionalResult);
        await Assert.That(healthy.OriginalInflightResult).IsEqualTo(recovered.OriginalInflightResult);
        if (!merge)
        {
            await ChunkMultiblockProcessRaw.RequireAsync(healthy, prepared: false, healthy: true);
        }
        await HealthyReceiptsAsync(root, healthy, prepared, merge);
        if (!ChunkMultiblockCrashProtocol.IsVerify(mode))
        {
            return;
        }
        var name = ChunkMultiblockCrashProtocol.IsFirstCold(mode) ? ChunkMultiblockCrashProtocol.FirstColdFile : ChunkMultiblockCrashProtocol.SecondColdFile;
        var cold = await ReadAsync(root, name, token);
        await Assert.That(JsonSerializer.Serialize(cold, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(healthy, JsonDefaults.Options));
        await ChunkMultiblockProcessRows.RequireAsync(cold, merge, prepared: false, healthy: true);
        if (!merge)
        {
            await ChunkMultiblockProcessRaw.RequireAsync(cold, prepared: false, healthy: true);
        }
        await HealthyReceiptsAsync(root, cold, prepared, merge);
    }

    private static async Task HealthyReceiptsAsync(string root, ChunkMultiblockCrashSnapshot state,
        ChunkMultiblockCrashSnapshot prepared, bool merge)
    {
        await ReceiptAsync(root, state.OriginalInflightResult!, prepared, SampleChunkCrashContract.InflightId,
            merge ? "mergeSampleChunkWindow" : "appendSamples",
            merge ? ChunkMultiblockCrashProtocol.RecoveredMergeRevision : ChunkMultiblockCrashProtocol.PreparedSequence);
        await ReceiptAsync(root, state.HealthyAppendResult!, prepared, ChunkMultiblockCrashData.FreshCommandId,
            "appendSamples", ChunkMultiblockCrashProtocol.SampleLimit);
        await ReceiptAsync(root, state.HealthyMergeResult!, prepared, ChunkMultiblockCrashData.HealthyMergeId,
            "mergeSampleChunkWindow",
            merge ? ChunkMultiblockCrashProtocol.HealthyMergeRevision : ChunkMultiblockCrashProtocol.HealthySealRevision);
        await Assert.That(state.StorageIncarnation).IsEqualTo(prepared.StorageIncarnation);
        await Assert.That(state.StorageDurability).IsEqualTo(prepared.StorageDurability);
    }

    private static async Task ReceiptAsync(string root, string bytes, ChunkMultiblockCrashSnapshot authority,
        Guid command, string kind, long revision)
    {
        var expectedToken = SampleChunkSnapshotFile.ReadExpectedToken(root, command,
            RecoveryExecutionOptions.DatabaseLimits().Value.MaxBatchBytes);
        var expected = new CommitReceipt(command, expectedToken, [new(kind, SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, revision)], authority.StorageDurability, (authority.StorageDurability == DurabilityProfile.Buffered ? 0 : 1));
        var actual = OrleansSerialization.Deserialize<Result<object>>(Convert.FromHexString(bytes)).Get<CommitReceipt>();
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    private static async Task<ChunkMultiblockCrashSnapshot> ReadAsync(string root, string name, CancellationToken token)
        => JsonSerializer.Deserialize<ChunkMultiblockCrashSnapshot>(await SampleChunkSnapshotFile.ReadAsync(Path.Combine(root, name),
            RecoveryExecutionOptions.DatabaseLimits().Value.MaxBatchBytes, token).ConfigureAwait(false), JsonDefaults.Options)
            ?? throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
}
