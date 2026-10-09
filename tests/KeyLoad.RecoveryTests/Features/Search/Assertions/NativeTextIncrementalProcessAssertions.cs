using KeyLoad.CrashHost.Features.Search;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeTextIncrementalProcessAssertions
{
    private const double SelectedScore = 1d / 61d;
    private const int RecordCount = 2;
    private const long NoAppliedPosition = 0;
    private const int MutationCount = 2;
    private const int HealthyMutationCount = 1;
    private const int FirstMutation = 0;
    private const int SecondMutation = 1;
    private const string PutKind = "putDocument";
    private const string DeleteKind = "deleteDocument";
    private const int DigestBytes = 32;
    private const ulong EnglishRecord = 1;
    private const ulong UkrainianRecord = 2;
    private const ulong NoPrevious = 0;
    private const ulong ChangedFirst = 6_144_113_096_060_154_513;
    private const ulong ChangedSecond = 7_802_930_873_135_693_526;
    private const ulong HealthyFirst = 4_523_150_942_467_080_430;
    private const ulong HealthySecond = 13_341_940_047_509_678_471;

    internal static async Task VerifyAsync(string root, string mode, CancellationToken token)
    {
        if (mode is NativeTextIncrementalCrashProtocol.Prepare or NativeTextIncrementalCrashProtocol.Fault)
        { return; }
        var original = await NativeTextIncrementalEvidenceFiles.ReadAsync<NativeTextIncrementalCrashOriginal>(root,
            NativeTextIncrementalCrashProtocol.OriginalFile, token);
        var recovered = await ReadAsync(root, NativeTextIncrementalCrashProtocol.RecoveredFile, token);
        var healthy = await ReadAsync(root, NativeTextIncrementalCrashProtocol.HealthyFile, token);
        await LiteralAsync(recovered, healthy: false);
        await LiteralAsync(healthy, healthy: true);
        await NativeTextIncrementalCheckpointAssertions.RequireAsync(root, original.Request, recovered, token);
        await Assert.That(NativeSerialization.Serialize(recovered.OriginalReceipt).SequenceEqual(NativeSerialization.Serialize(original.Receipt))).IsTrue();
        await Assert.That(original.Receipt.CommandId).IsEqualTo(original.Mutation.CommandId);
        await Assert.That(original.Mutation.Partition).IsEqualTo(NativeTextIncrementalCrashProtocol.Partition);
        await Assert.That(original.Receipt.Token.AtomicPartitionId).IsEqualTo(NativeTextIncrementalCrashProtocol.Partition.AtomicPartitionId);
        await Assert.That(original.Receipt.Mutations.Length).IsEqualTo(MutationCount);
        await MutationAsync(original.Receipt.Mutations[FirstMutation], new(PutKind, NativeTextIncrementalCrashProtocol.Collection,
            NativeTextIncrementalCrashProtocol.Ukrainian, NativeTextIncrementalCrashProtocol.ChangedRevision));
        await MutationAsync(original.Receipt.Mutations[SecondMutation], new(DeleteKind, NativeTextIncrementalCrashProtocol.Collection,
            NativeTextIncrementalCrashProtocol.English, NativeTextIncrementalCrashProtocol.ChangedRevision));
        var healthyOriginal = await NativeTextIncrementalEvidenceFiles.ReadAsync<NativeTextIncrementalCrashOriginal>(root,
            NativeTextIncrementalCrashProtocol.RequestFile, token);
        await NativeTextIncrementalCheckpointAssertions.RequireAsync(root, healthyOriginal.Request, healthy, token);
        await Assert.That(healthy.OriginalReceipt.CommandId).IsEqualTo(healthyOriginal.Mutation.CommandId);
        await Assert.That(healthyOriginal.Mutation.Partition).IsEqualTo(NativeTextIncrementalCrashProtocol.Partition);
        await Assert.That(healthy.OriginalReceipt.Token.AtomicPartitionId).IsEqualTo(NativeTextIncrementalCrashProtocol.Partition.AtomicPartitionId);
        await Assert.That(healthy.OriginalReceipt.Mutations.Length).IsEqualTo(HealthyMutationCount);
        await MutationAsync(healthy.OriginalReceipt.Mutations[FirstMutation], new(PutKind,
            NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.Ukrainian,
            NativeTextIncrementalCrashProtocol.HealthyRevision));
        await Assert.That(healthy.Complete.ThroughSequence).IsGreaterThan(recovered.Complete.ThroughSequence);
        await Assert.That(healthy.Complete.IndexSha256).IsNotEqualTo(recovered.Complete.IndexSha256);
        if (mode != NativeTextIncrementalCrashProtocol.Verify)
        { return; }
        var verified = await ReadAsync(root, NativeTextIncrementalCrashProtocol.VerifiedFile, token);
        await LiteralAsync(verified, healthy: true);
        await NativeTextIncrementalCheckpointAssertions.RequireAsync(root, healthyOriginal.Request, verified, token);
        await Assert.That(NativeSerialization.Serialize(verified.Checkpoint).SequenceEqual(
            NativeSerialization.Serialize(healthy.Checkpoint))).IsTrue();
        await Assert.That(verified.Complete.IndexSha256).IsEqualTo(healthy.Complete.IndexSha256);
        await Assert.That(verified.Complete.ThroughSequence).IsEqualTo(healthy.Complete.ThroughSequence);
        await Assert.That(verified.AppliedPosition).IsEqualTo(healthy.AppliedPosition);
        await Assert.That(verified.StorePosition).IsEqualTo(healthy.StorePosition);
        await Assert.That(verified.Canonical.RecordCount).IsEqualTo(healthy.Canonical.RecordCount);
        await Assert.That(verified.Canonical.Sha256.SequenceEqual(healthy.Canonical.Sha256)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(verified.OriginalReceipt).SequenceEqual(NativeSerialization.Serialize(healthy.OriginalReceipt))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(verified.Records).SequenceEqual(JsonDefaults.Serialize(healthy.Records))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(verified.Postings).SequenceEqual(JsonDefaults.Serialize(healthy.Postings))).IsTrue();
    }

    private static async Task LiteralAsync(NativeTextIncrementalCrashResult actual, bool healthy)
    {
        var revision = healthy ? NativeTextIncrementalCrashProtocol.HealthyRevision : NativeTextIncrementalCrashProtocol.ChangedRevision;
        var expected = new DocumentResult(new(NativeTextIncrementalCrashProtocol.Partition,
            NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.Ukrainian), revision,
            healthy ? NativeTextIncrementalCrashProtocol.HealthyJson : NativeTextIncrementalCrashProtocol.ChangedJson, false, []);
        await Assert.That(JsonDefaults.Serialize(actual.Ukrainian).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.English).IsNull();
        RankedDocument[] selected = [new(expected, SelectedScore)];
        await Assert.That(JsonDefaults.Serialize(actual.SelectedPage).SequenceEqual(JsonDefaults.Serialize(selected))).IsTrue();
        await Assert.That(actual.RemovedUkrainian).IsEmpty();
        await Assert.That(actual.RemovedEnglish).IsEmpty();
        await Assert.That(actual.Records.Length).IsEqualTo(RecordCount);
        var active = actual.Records.Single(record => record.Reference.Id == NativeTextIncrementalCrashProtocol.Ukrainian);
        var deleted = actual.Records.Single(record => record.Reference.Id == NativeTextIncrementalCrashProtocol.English);
        await Assert.That(active.Id).IsEqualTo(UkrainianRecord);
        await Assert.That(active.Reference).IsEqualTo(expected.Reference);
        await Assert.That(active.Revision).IsEqualTo(revision);
        await Assert.That(active.Deleted).IsFalse();
        await Assert.That(active.CanonicalSha256.Length).IsEqualTo(DigestBytes);
        await Assert.That(deleted.Id).IsEqualTo(EnglishRecord);
        await Assert.That(deleted.Reference).IsEqualTo(new EntityRef(NativeTextIncrementalCrashProtocol.Partition,
            NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.English));
        await Assert.That(deleted.Revision).IsEqualTo(NativeTextIncrementalCrashProtocol.ChangedRevision);
        await Assert.That(deleted.Deleted).IsTrue();
        await Assert.That(deleted.CanonicalSha256.Length).IsEqualTo(DigestBytes);
        var first = healthy ? HealthyFirst : ChangedFirst;
        var second = healthy ? HealthySecond : ChangedSecond;
        NativeTextIncrementalPosting[] postings = [new(first, UkrainianRecord, NoPrevious), new(second, UkrainianRecord, first)];
        await Assert.That(JsonDefaults.Serialize(actual.Postings).SequenceEqual(JsonDefaults.Serialize(postings))).IsTrue();
        await Assert.That(actual.Complete.Checkpoint).IsEqualTo(actual.Complete.ThroughSequence);
        await Assert.That(actual.Complete.OriginalCheckpointIntent).IsNull();
        await Assert.That(actual.Complete.TrackedRecords).IsEqualTo(RecordCount);
        await Assert.That(actual.AppliedPosition).IsGreaterThan(NoAppliedPosition);
        await Assert.That(actual.Canonical.Sha256.Length).IsEqualTo(DigestBytes);
    }

    private static async Task MutationAsync(MutationReceipt actual, MutationReceipt expected)
        => await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    private static async Task<NativeTextIncrementalCrashResult> ReadAsync(string root, string file, CancellationToken token)
        => await NativeTextIncrementalEvidenceFiles.ReadAsync<NativeTextIncrementalCrashResult>(root, file, token);
}
