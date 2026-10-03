using System.Buffers.Binary;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataMutationTests
{
    private const long InitialTerm = 2;
    private const long OriginalEntryTerm = 1;
    private const long ChangedEntryTerm = 2;
    private const int FirstIndex = 1;
    private const int ThirdIndex = 3;
    private const string ValidDocumentJson = "{}";

    [Test]
    public async Task DirectChangeDeleteAndRepairAlwaysRevalidateTheStoredEntry()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            var before = fixture.Store.GetReadDiagnostics();

            CommitEntry(fixture.Store, new(FirstIndex, ChangedEntryTerm, null));
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(ChangedEntryTerm);
            DeleteEntry(fixture.Store, FirstIndex);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(FirstIndex)).Code)
                .IsEqualTo(ErrorCode.Corruption);
            CommitEntry(fixture.Store, new(FirstIndex, ChangedEntryTerm, null));
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(ChangedEntryTerm);

            var after = fixture.Store.GetReadDiagnostics();
            await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(3);
        });
    }

    [Test]
    public async Task MalformedNestedOperationCannotBeSkippedOnTheStrictTermMiss()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            var operation = fixture.Operation(ReplicaTermMetadataFixture.AtomicBatch(ValidDocumentJson));
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, operation)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            var entry = new ReplicaEntry(FirstIndex, OriginalEntryTerm, operation);
            var valid = ReplicaProtocolCodec.Serialize(entry);
            await Assert.That(ReplicaProtocolCodec.Deserialize<ReplicaEntry>(valid).Operation!.Kind).IsEqualTo(OperationKind.Batch);
            var malformed = UnknownNestedOperation(entry);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(malformed, fixture.Configuration.MaxAppendEntries)).Code)
                .IsEqualTo(ErrorCode.Corruption);
            fixture.Store.Commit((transaction, _) =>
            {
                transaction.Put(ReplicaProtocol.EntryStorageKey(FirstIndex), malformed);
                return true;
            });

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(FirstIndex)).Code)
                .IsEqualTo(ErrorCode.Corruption);
        });
    }

    [Test]
    [Arguments(2, 1)]
    [Arguments(1, 0)]
    [Arguments(1, 3)]
    public async Task StoredIndexAndTermMustRemainValidOnEveryMiss(long storedIndex, long storedTerm)
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            CommitEntryAtRequestedKey(fixture.Store, FirstIndex, new(storedIndex, storedTerm, null));

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(FirstIndex)).Code)
                .IsEqualTo(ErrorCode.Corruption);
        });
    }

    [Test]
    public async Task ReplacingAnUncommittedSuffixHidesItsPreviouslyObservedTerm()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(FirstIndex, 1, null), new(2, 1, null), new(ThirdIndex, 1, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(ThirdIndex)).IsEqualTo(1);

            fixture.Log.SaveTermAndVote(InitialTerm, null);
            fixture.Log.Append([new(2, InitialTerm, null)]);

            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Log.TermAt(ThirdIndex)).Code)
                .IsEqualTo(ErrorCode.NotFound);
            await Assert.That(fixture.Log.TermAt(2)).IsEqualTo(InitialTerm);
        });
    }

    [Test]
    public async Task NormalTermAdvanceAndCommitCannotReuseThePreviousCutObservation()
    {
        await ReplicaTermMetadataFixture.RunAsync(async fixture =>
        {
            fixture.Log.SaveTermAndVote(OriginalEntryTerm, null);
            fixture.Log.Append([new(FirstIndex, OriginalEntryTerm, null)]);
            fixture.Log.Commit(FirstIndex);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            var beforeAdvance = fixture.Store.GetReadDiagnostics();

            fixture.Log.SaveTermAndVote(InitialTerm, ReplicaTermMetadataFixture.VoterA);
            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            fixture.Log.Append([new(2, InitialTerm, null)]);
            fixture.Log.Commit(2);

            await Assert.That(fixture.Log.TermAt(FirstIndex)).IsEqualTo(OriginalEntryTerm);
            await Assert.That(fixture.Log.TermAt(2)).IsEqualTo(InitialTerm);
            var afterAdvance = fixture.Store.GetReadDiagnostics();
            await Assert.That(afterAdvance.BorrowedPointLookups - beforeAdvance.BorrowedPointLookups).IsEqualTo(3);
        });
    }

    private static void CommitEntry(ZoneTreeStore store, ReplicaEntry entry)
        => store.Commit((transaction, _) => { transaction.Put(ReplicaProtocol.EntryStorageKey(entry.Index), ReplicaProtocolCodec.Serialize(entry)); return true; });

    private static void CommitEntryAtRequestedKey(ZoneTreeStore store, long requestedIndex, ReplicaEntry entry)
        => store.Commit((transaction, _) => { transaction.Put(ReplicaProtocol.EntryStorageKey(requestedIndex), ReplicaProtocolCodec.Serialize(entry)); return true; });

    private static byte[] UnknownNestedOperation(ReplicaEntry entry)
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(ReplicaEntry), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            builder.Services.AddSingleton<ReplicaEntryInspectionCodec>();
            builder.Services.AddSingleton(new ReplicaMaximumOperationCodec(ReplicaMaximumPayload.UnknownField));
            builder.Configure(options =>
            {
                options.FieldCodecs.Add(typeof(ReplicaEntryInspectionCodec));
                options.FieldCodecs.Add(typeof(ReplicaMaximumOperationCodec));
            });
        });
        // Route the entry's nested operation through the provider so the malformed field codec is honored.
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = entry });
        var framed = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(framed, ReplicaProtocol.PayloadMagic);
        body.CopyTo(framed, ReplicaProtocol.PayloadPrefixBytes);
        return framed;
    }

    private static void DeleteEntry(ZoneTreeStore store, long index)
        => store.Commit((transaction, _) => { transaction.Delete(ReplicaProtocol.EntryStorageKey(index)); return true; });
}
