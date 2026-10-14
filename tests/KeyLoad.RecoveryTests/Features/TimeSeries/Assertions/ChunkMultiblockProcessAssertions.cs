using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class ChunkMultiblockProcessAssertions
{
    private const long OwnershipEpoch = 1;
    internal static async Task VerifyAsync(string root, string mode, bool merge, CancellationToken token)
    {
        if (ChunkMultiblockCrashProtocol.IsFault(mode))
        { return; }
        var prepared = await ReadAsync(root, SampleChunkCrashContract.PreparedFile, token);
        await ChunkMultiblockProcessRows.RequireAsync(prepared, merge, prepared: true, healthy: false);
        await Assert.That(prepared.OriginalInflightResult).IsNull();
        if (ChunkMultiblockCrashProtocol.IsPrepare(mode))
        { return; }
        var recovered = await ReadAsync(root, SampleChunkCrashContract.RecoveredFile, token);
        await ChunkMultiblockProcessRows.RequireAsync(recovered, merge, prepared: false, healthy: false);
        await Assert.That(recovered.Position).IsEqualTo(checked(prepared.Position + SampleChunkCrashContract.PositionStep));
        await Assert.That(recovered.RawImage).IsEqualTo(prepared.RawImage);
        await Assert.That(recovered.OriginalAcknowledgedReceipt).IsEqualTo(prepared.OriginalAcknowledgedReceipt);
        await Assert.That(recovered.OriginalAdditionalResult).IsEqualTo(prepared.OriginalAdditionalResult);
        await Assert.That(recovered.OriginalInflightResult).IsNotNull();
        await ReceiptAsync(recovered.OriginalInflightResult!, prepared, SampleChunkCrashContract.InflightId,
            recovered.Position, merge ? "mergeSampleChunkWindow" : "sealSampleChunkWindow",
            merge ? ChunkMultiblockCrashProtocol.RecoveredMergeRevision : ChunkMultiblockCrashProtocol.RecoveredSealRevision);
        var healthy = await ReadAsync(root, SampleChunkCrashContract.HealthyFile, token);
        await ChunkMultiblockProcessRows.RequireAsync(healthy, merge, prepared: false, healthy: true);
        await Assert.That(healthy.Position).IsEqualTo(checked(recovered.Position + ChunkMultiblockCrashProtocol.HealthyCommits));
        await Assert.That(healthy.OriginalAcknowledgedReceipt).IsEqualTo(prepared.OriginalAcknowledgedReceipt);
        await Assert.That(healthy.OriginalAdditionalResult).IsEqualTo(prepared.OriginalAdditionalResult);
        await Assert.That(healthy.OriginalInflightResult).IsEqualTo(recovered.OriginalInflightResult);
        await HealthyReceiptsAsync(healthy, prepared, recovered.Position, merge);
        if (!ChunkMultiblockCrashProtocol.IsVerify(mode))
        { return; }
        var name = ChunkMultiblockCrashProtocol.IsFirstCold(mode) ? ChunkMultiblockCrashProtocol.FirstColdFile : ChunkMultiblockCrashProtocol.SecondColdFile;
        var cold = await ReadAsync(root, name, token);
        await Assert.That(JsonSerializer.Serialize(cold, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(healthy, JsonDefaults.Options));
        await ChunkMultiblockProcessRows.RequireAsync(cold, merge, prepared: false, healthy: true);
        await HealthyReceiptsAsync(cold, prepared, recovered.Position, merge);
    }

    private static async Task HealthyReceiptsAsync(ChunkMultiblockCrashSnapshot state,
        ChunkMultiblockCrashSnapshot prepared, long recoveredPosition, bool merge)
    {
        await ReceiptAsync(state.OriginalInflightResult!, prepared, SampleChunkCrashContract.InflightId,
            recoveredPosition, merge ? "mergeSampleChunkWindow" : "sealSampleChunkWindow",
            merge ? ChunkMultiblockCrashProtocol.RecoveredMergeRevision : ChunkMultiblockCrashProtocol.RecoveredSealRevision);
        await ReceiptAsync(state.HealthyAppendResult!, prepared, ChunkMultiblockCrashData.FreshCommandId,
            checked(recoveredPosition + SampleChunkCrashContract.PositionStep), "appendSamples", ChunkMultiblockCrashProtocol.SampleLimit);
        await ReceiptAsync(state.HealthyMergeResult!, prepared, ChunkMultiblockCrashData.HealthyMergeId,
            checked(recoveredPosition + ChunkMultiblockCrashProtocol.HealthyCommits), "mergeSampleChunkWindow",
            merge ? ChunkMultiblockCrashProtocol.HealthyMergeRevision : ChunkMultiblockCrashProtocol.HealthySealRevision);
        await Assert.That(state.StorageIncarnation).IsEqualTo(prepared.StorageIncarnation);
        await Assert.That(state.StorageDurability).IsEqualTo(prepared.StorageDurability);
    }

    private static async Task ReceiptAsync(string bytes, ChunkMultiblockCrashSnapshot authority,
        Guid command, long position, string kind, long revision)
    {
        var expected = new CommitReceipt(command,
            new(authority.StorageIncarnation, SampleChunkCrashContract.Partition.AtomicPartitionId, position, OwnershipEpoch),
            [new(kind, SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, revision)], authority.StorageDurability);
        var actual = NativeSerialization.Deserialize<OperationResult>(Convert.FromHexString(bytes)).Get<CommitReceipt>();
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    private static async Task<ChunkMultiblockCrashSnapshot> ReadAsync(string root, string name, CancellationToken token)
        => JsonSerializer.Deserialize<ChunkMultiblockCrashSnapshot>(await SampleChunkSnapshotFile.ReadAsync(Path.Combine(root, name),
            RecoveryExecutionOptions.DatabaseLimits().Value.MaxBatchBytes, token).ConfigureAwait(false), JsonDefaults.Options)
            ?? throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
}
