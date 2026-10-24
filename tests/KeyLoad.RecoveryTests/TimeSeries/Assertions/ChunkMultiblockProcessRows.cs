using System.Text.Json;

namespace KeyLoad.RecoveryTests.TimeSeries;

internal static class ChunkMultiblockProcessRows
{
    internal static async Task RequireAsync(ChunkMultiblockCrashSnapshot actual, bool merge, bool prepared, bool healthy)
    {
        var rows = ChunkMultiblockCrashData.Expected(healthy, prepared, merge);
        var revision = prepared ? (merge ? ChunkMultiblockCrashProtocol.PreparedMergeRevision : ChunkMultiblockCrashProtocol.PreparedSealRevision)
            : healthy ? (merge ? ChunkMultiblockCrashProtocol.HealthyMergeRevision : ChunkMultiblockCrashProtocol.HealthySealRevision)
            : merge ? ChunkMultiblockCrashProtocol.RecoveredMergeRevision : ChunkMultiblockCrashProtocol.RecoveredSealRevision;
        var generation = prepared ? (merge ? SampleChunkCrashContract.SealedGeneration : ChunkMultiblockCrashProtocol.OpenGeneration)
            : healthy ? (merge ? ChunkMultiblockCrashProtocol.HealthyMergeGeneration : ChunkMultiblockCrashProtocol.HealthySealGeneration)
            : merge ? ChunkMultiblockCrashProtocol.RecoveredMergeGeneration : ChunkMultiblockCrashProtocol.RecoveredSealGeneration;
        var model = new SampleChunkWindowResult(SampleChunkCrashContract.WindowId, SampleChunkCrashContract.From,
            SampleChunkCrashContract.Until, generation, revision, healthy ? ChunkMultiblockCrashProtocol.HealthySequence
                : prepared && !merge ? ChunkMultiblockCrashProtocol.PreparedAutomaticSealSequence
                : ChunkMultiblockCrashProtocol.PreparedSequence, null, [.. rows], actual.Position);
        await Assert.That(JsonSerializer.Serialize(actual.Window, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(model, JsonDefaults.Options));
        await Assert.That(JsonSerializer.Serialize(actual.RawRows, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(rows, JsonDefaults.Options));
        await Assert.That(actual.EncodedBlocks.Length).IsEqualTo(prepared ? (merge ? 1 : 0) : !healthy && !merge ? 1 : ChunkMultiblockCrashProtocol.PublishedBlocks);
        for (var index = 0; index < rows.Length; index++)
        {
            await Assert.That(actual.RawRows[index].Sample.Timestamp.Ticks).IsEqualTo(rows[index].Sample.Timestamp.Ticks);
            await Assert.That(actual.RawRows[index].Sample.Timestamp.Offset).IsEqualTo(rows[index].Sample.Timestamp.Offset);
            await Assert.That(BitConverter.DoubleToInt64Bits(actual.RawRows[index].Sample.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(rows[index].Sample.Value));
        }
    }
}
