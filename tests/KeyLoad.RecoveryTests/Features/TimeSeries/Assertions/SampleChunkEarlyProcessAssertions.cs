using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class SampleChunkEarlyProcessAssertions
{
    internal static async Task VerifyAsync(string root, string mode, SampleChunkEarlyCut cut, CancellationToken token)
    {
        if (SampleChunkEarlyCrashProtocol.IsFault(mode))
        { return; }
        var prepared = await ReadAsync(root, SampleChunkCrashContract.PreparedFile, token);
        if (SampleChunkEarlyCrashProtocol.IsPrepare(mode))
        {
            await PreparedAsync(prepared, cut);
            return;
        }
        var recovered = await ReadAsync(root, SampleChunkCrashContract.RecoveredFile, token);
        var healthy = await ReadAsync(root, SampleChunkCrashContract.HealthyFile, token);
        await Assert.That(recovered.Position > prepared.Position).IsTrue();
        await Assert.That(recovered.OriginalAcknowledgedResult).IsEqualTo(prepared.OriginalAcknowledgedResult);
        await RecoveredAsync(recovered, cut);
        await Assert.That(recovered.OriginalInflightResult).IsNotNull();
        if (cut == SampleChunkEarlyCut.Open)
        { await Assert.That(recovered.RawImage).IsEqualTo(prepared.RawImage); }
        var result = NativeSerialization.Deserialize<OperationResult>(Convert.FromHexString(recovered.OriginalInflightResult!));
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(SampleChunkCrashContract.InflightId);
        await Assert.That(receipt.Token.Position).IsEqualTo(recovered.Position);
        await LiteralAsync(healthy, SampleChunkCrashContract.MergedRevision, SampleChunkCrashContract.MergedGeneration,
            SampleChunkEarlyCrashProtocol.CorrectedSequence);
        await Assert.That(healthy.Position > recovered.Position).IsTrue();
        await Assert.That(healthy.OriginalAcknowledgedResult).IsEqualTo(prepared.OriginalAcknowledgedResult);
        await Assert.That(healthy.OriginalInflightResult).IsEqualTo(recovered.OriginalInflightResult);
        if (!SampleChunkEarlyCrashProtocol.IsVerify(mode))
        { return; }
        var final = await ReadAsync(root, SampleChunkCrashContract.FinalFile, token);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(final)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(healthy)));
        await LiteralAsync(final, SampleChunkCrashContract.MergedRevision, SampleChunkCrashContract.MergedGeneration,
            SampleChunkEarlyCrashProtocol.CorrectedSequence);
    }

    private static async Task PreparedAsync(SampleChunkEarlyCrashSnapshot actual, SampleChunkEarlyCut cut)
    {
        var original = NativeSerialization.Deserialize<OperationResult>(Convert.FromHexString(actual.OriginalAcknowledgedResult));
        var expected = new ResourceDefinition(SampleChunkCrashContract.Set, ResourceKind.TimeSeries,
            SampleChunkCrashContract.Partition.TransactionDomainId);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(original.Get<ResourceDefinition>())))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await Assert.That(actual.OriginalInflightResult).IsNull();
        if (cut == SampleChunkEarlyCut.Open)
        {
            await Assert.That(actual.Window).IsNull();
            await Assert.That(actual.RawRows).IsEmpty();
            await Assert.That(actual.RawImage).IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(Array.Empty<string[]>())));
            return;
        }
        await LiteralAsync(actual, cut == SampleChunkEarlyCut.Append ? SampleChunkEarlyCrashProtocol.OpenRevision
            : SampleChunkCrashContract.SealedRevision, cut == SampleChunkEarlyCut.Append
                ? SampleChunkEarlyCrashProtocol.OpenGeneration : SampleChunkCrashContract.SealedGeneration,
            cut == SampleChunkEarlyCut.Append ? SampleChunkEarlyCrashProtocol.EmptySequence
                : SampleChunkEarlyCrashProtocol.InitialSequence);
    }

    private static Task RecoveredAsync(SampleChunkEarlyCrashSnapshot actual, SampleChunkEarlyCut cut)
        => LiteralAsync(actual, cut switch
        {
            SampleChunkEarlyCut.Open => SampleChunkEarlyCrashProtocol.OpenRevision,
            SampleChunkEarlyCut.Append => SampleChunkCrashContract.AppendedRevision,
            SampleChunkEarlyCut.Correction => SampleChunkCrashContract.CorrectedRevision,
            _ => throw new InvalidOperationException(SampleChunkCrashContract.Invalid)
        }, cut == SampleChunkEarlyCut.Correction ? SampleChunkCrashContract.SealedGeneration
            : SampleChunkEarlyCrashProtocol.OpenGeneration, cut == SampleChunkEarlyCut.Open
            ? SampleChunkEarlyCrashProtocol.EmptySequence : cut == SampleChunkEarlyCut.Append
                ? SampleChunkEarlyCrashProtocol.InitialSequence : SampleChunkEarlyCrashProtocol.CorrectedSequence);

    private static async Task LiteralAsync(SampleChunkEarlyCrashSnapshot actual, long revision, long generation, long sequence)
    {
        var rows = new List<SampleRecord>();
        if (sequence >= SampleChunkEarlyCrashProtocol.InitialSequence)
        {
            rows.Add(new(SampleChunkCrashContract.Series, SampleChunkCrashContract.First,
                SampleChunkEarlyCrashProtocol.FirstSequence, SampleChunkCrashContract.Tags));
            rows.Add(new(SampleChunkCrashContract.Series, SampleChunkCrashContract.Equal,
                SampleChunkEarlyCrashProtocol.InitialSequence, SampleChunkCrashContract.Tags));
        }
        if (sequence == SampleChunkEarlyCrashProtocol.CorrectedSequence)
        { rows.Add(new(SampleChunkCrashContract.Series, SampleChunkCrashContract.Late, sequence, SampleChunkCrashContract.Tags)); }
        var expected = new SampleChunkWindowResult(SampleChunkCrashContract.WindowId, SampleChunkCrashContract.From,
            SampleChunkCrashContract.Until, generation, revision, sequence, null, [.. rows], actual.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual.Window)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual.RawRows)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(rows)));
    }

    private static async Task<SampleChunkEarlyCrashSnapshot> ReadAsync(string root, string name, CancellationToken token)
    {
        var path = Path.Combine(root, name);
        return JsonSerializer.Deserialize<SampleChunkEarlyCrashSnapshot>(
            await SampleChunkSnapshotFile.ReadAsync(path,
                RecoveryExecutionOptions.DatabaseLimits().Value.MaxBatchBytes, token).ConfigureAwait(false), JsonDefaults.Options)
            ?? throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
    }
}
