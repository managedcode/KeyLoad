using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server.Search;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class TextIncrementalProcessAssertions
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
        if (mode is TextIncrementalCrashProtocol.Prepare or TextIncrementalCrashProtocol.Fault)
        {
            return;
        }
        var original = await TextIncrementalEvidenceFiles.ReadAsync<TextIncrementalCrashOriginal>(root,
            TextIncrementalCrashProtocol.OriginalFile, token);
        var recovered = await ReadAsync(root, TextIncrementalCrashProtocol.RecoveredFile, token);
        var healthy = await ReadAsync(root, TextIncrementalCrashProtocol.HealthyFile, token);
        var lifetime = await ReadLifetimeAsync(root, token);
        await LiteralAsync(recovered, lifetime, healthy: false);
        await LiteralAsync(healthy, lifetime, healthy: true);
        await TextIncrementalCheckpointAssertions.RequireAsync(root, original.Request, recovered, token);
        await Assert.That(OrleansSerialization.Serialize(recovered.OriginalReceipt).SequenceEqual(OrleansSerialization.Serialize(original.Receipt))).IsTrue();
        await Assert.That(original.Receipt.CommandId).IsEqualTo(original.Mutation.CommandId);
        await Assert.That(original.Mutation.Partition).IsEqualTo(TextIncrementalCrashProtocol.Partition);
        await Assert.That(original.Receipt.Token.AtomicPartitionId).IsEqualTo(TextIncrementalCrashProtocol.Partition.AtomicPartitionId);
        await Assert.That(original.Receipt.Mutations.Length).IsEqualTo(MutationCount);
        await MutationAsync(original.Receipt.Mutations[FirstMutation], new(PutKind, TextIncrementalCrashProtocol.Collection,
            TextIncrementalCrashProtocol.Ukrainian, TextIncrementalCrashProtocol.ChangedRevision));
        await MutationAsync(original.Receipt.Mutations[SecondMutation], new(DeleteKind, TextIncrementalCrashProtocol.Collection,
            TextIncrementalCrashProtocol.English, TextIncrementalCrashProtocol.ChangedRevision));
        var healthyOriginal = await TextIncrementalEvidenceFiles.ReadAsync<TextIncrementalCrashOriginal>(root,
            TextIncrementalCrashProtocol.RequestFile, token);
        await TextIncrementalCheckpointAssertions.RequireAsync(root, healthyOriginal.Request, healthy, token);
        await Assert.That(healthy.OriginalReceipt.CommandId).IsEqualTo(healthyOriginal.Mutation.CommandId);
        await Assert.That(healthyOriginal.Mutation.Partition).IsEqualTo(TextIncrementalCrashProtocol.Partition);
        await Assert.That(healthy.OriginalReceipt.Token.AtomicPartitionId).IsEqualTo(TextIncrementalCrashProtocol.Partition.AtomicPartitionId);
        await Assert.That(healthy.OriginalReceipt.Mutations.Length).IsEqualTo(HealthyMutationCount);
        await MutationAsync(healthy.OriginalReceipt.Mutations[FirstMutation], new(PutKind,
            TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.Ukrainian,
            TextIncrementalCrashProtocol.HealthyRevision));
        await Assert.That(healthy.Complete.ThroughSequence).IsGreaterThan(recovered.Complete.ThroughSequence);
        await Assert.That(healthy.Complete.IndexSha256).IsNotEqualTo(recovered.Complete.IndexSha256);
        if (mode != TextIncrementalCrashProtocol.Verify)
        {
            return;
        }
        var verified = await ReadAsync(root, TextIncrementalCrashProtocol.VerifiedFile, token);
        await LiteralAsync(verified, lifetime, healthy: true);
        await TextIncrementalCheckpointAssertions.RequireAsync(root, healthyOriginal.Request, verified, token);
        await Assert.That(OrleansSerialization.Serialize(verified.Checkpoint).SequenceEqual(
            OrleansSerialization.Serialize(healthy.Checkpoint))).IsTrue();
        await Assert.That(verified.Complete.IndexSha256).IsEqualTo(healthy.Complete.IndexSha256);
        await Assert.That(verified.Complete.ThroughSequence).IsEqualTo(healthy.Complete.ThroughSequence);
        await Assert.That(verified.AppliedPosition).IsEqualTo(healthy.AppliedPosition);
        await Assert.That(verified.StorePosition).IsEqualTo(healthy.StorePosition);
        await Assert.That(verified.Canonical.RecordCount).IsEqualTo(healthy.Canonical.RecordCount);
        await Assert.That(verified.Canonical.Sha256.SequenceEqual(healthy.Canonical.Sha256)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(verified.OriginalReceipt).SequenceEqual(OrleansSerialization.Serialize(healthy.OriginalReceipt))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(verified.Records).SequenceEqual(JsonDefaults.Serialize(healthy.Records))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(verified.Postings).SequenceEqual(JsonDefaults.Serialize(healthy.Postings))).IsTrue();
    }

    internal static async Task<EntityRefLifetime> ReadLifetimeAsync(string root, CancellationToken token)
    {
        var creation = await TextIncrementalEvidenceFiles.ReadAsync<StoredOutcome>(root,
            TextIncrementalCrashProtocol.CreationFile, token);
        var original = creation.PreparedCommand;
        var partition = TextIncrementalCrashProtocol.Partition;
        var definition = new ResourceDefinition(TextIncrementalCrashProtocol.Collection,
            ResourceKind.Collection, partition.TransactionDomainId);
        var expected = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await Assert.That(creation.Result.IsSuccess).IsTrue();
        await Assert.That(original.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(original.UserId).IsEqualTo(CrashFixtureValues.Principal);
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(JsonDefaults.Serialize(((ConfigureResourceRequest)original.Value.Input))
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var lifetime = ResourceLifetimeIdentity.Create(original, partition.TenantId, partition.DatabaseId, definition);
        return new(lifetime.Incarnation.Value);
    }

    private static async Task LiteralAsync(TextIncrementalCrashResult actual, EntityRefLifetime lifetime, bool healthy)
    {
        var revision = healthy ? TextIncrementalCrashProtocol.HealthyRevision : TextIncrementalCrashProtocol.ChangedRevision;
        var expected = new DocumentResult(new(TextIncrementalCrashProtocol.Partition,
            TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.Ukrainian)
        {
            Lifetime = lifetime
        }, revision,
            healthy ? TextIncrementalCrashProtocol.HealthyJson : TextIncrementalCrashProtocol.ChangedJson, false, []);
        await Assert.That(JsonDefaults.Serialize(actual.Ukrainian).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.English).IsNull();
        RankedDocument[] selected = [new(expected, SelectedScore)];
        await Assert.That(JsonDefaults.Serialize(actual.SelectedPage).SequenceEqual(JsonDefaults.Serialize(selected))).IsTrue();
        await Assert.That(actual.RemovedUkrainian).IsEmpty();
        await Assert.That(actual.RemovedEnglish).IsEmpty();
        await Assert.That(actual.Records.Length).IsEqualTo(RecordCount);
        var active = actual.Records.Single(record => record.Reference.Id == TextIncrementalCrashProtocol.Ukrainian);
        var deleted = actual.Records.Single(record => record.Reference.Id == TextIncrementalCrashProtocol.English);
        await Assert.That(active.Id).IsEqualTo(UkrainianRecord);
        await Assert.That(active.Reference).IsEqualTo(expected.Reference);
        await Assert.That(active.Revision).IsEqualTo(revision);
        await Assert.That(active.Deleted).IsFalse();
        await Assert.That(active.CanonicalSha256.Length).IsEqualTo(DigestBytes);
        await Assert.That(deleted.Id).IsEqualTo(EnglishRecord);
        await Assert.That(deleted.Reference).IsEqualTo(new EntityRef(TextIncrementalCrashProtocol.Partition,
            TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.English)
        {
            Lifetime = lifetime
        });
        await Assert.That(deleted.Revision).IsEqualTo(TextIncrementalCrashProtocol.ChangedRevision);
        await Assert.That(deleted.Deleted).IsTrue();
        await Assert.That(deleted.CanonicalSha256.Length).IsEqualTo(DigestBytes);
        var first = healthy ? HealthyFirst : ChangedFirst;
        var second = healthy ? HealthySecond : ChangedSecond;
        TextIncrementalPosting[] postings = [new(first, UkrainianRecord, NoPrevious), new(second, UkrainianRecord, first)];
        await Assert.That(JsonDefaults.Serialize(actual.Postings).SequenceEqual(JsonDefaults.Serialize(postings))).IsTrue();
        await Assert.That(actual.Complete.Checkpoint).IsEqualTo(actual.Complete.ThroughSequence);
        await Assert.That(actual.Complete.OriginalCheckpointIntent).IsNull();
        await Assert.That(actual.Complete.TrackedRecords).IsEqualTo(RecordCount);
        await Assert.That(actual.AppliedPosition).IsGreaterThan(NoAppliedPosition);
        await Assert.That(actual.Canonical.Sha256.Length).IsEqualTo(DigestBytes);
    }

    private static async Task MutationAsync(MutationReceipt actual, MutationReceipt expected)
        => await Assert.That(OrleansSerialization.Serialize(actual).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();

    private static async Task<TextIncrementalCrashResult> ReadAsync(string root, string file, CancellationToken token)
        => await TextIncrementalEvidenceFiles.ReadAsync<TextIncrementalCrashResult>(root, file, token);
}
