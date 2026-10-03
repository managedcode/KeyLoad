using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Actual generated writers are the oracle for inspection; fixtures never recreate successful records.
internal sealed class ReplicaGeneratedCodecCompatibilityTests
{
    private const string Voter = "native-generated-voter";
    private const string OtherVoter = "native-generated-other";
    private const string Principal = "native-generated-principal";
    private const string PublicPayload = " true ";
    private const string InvalidPublicPayload = "{";
    private const string GuidFormat = "N";
    private const int MaximumEntries = 2;
    private const long Term = 3;
    private const long Index = 1;
    private const long ImageLength = 4;
    private static readonly byte[] ChunkBytes = [0x00, 0x81, 0xfe, 0xff];

    [Test]
    public async Task AcIs005GeneratedVoteAndSnapshotRequestsPreserveTheirBodyFields()
    {
        var transferId = Guid.NewGuid();
        var checksum = Convert.ToHexStringLower(SHA256.HashData(ChunkBytes));
        var imageFile = transferId.ToString(GuidFormat) + ReplicaProtocol.SnapshotExtension;
        var snapshot = new ReplicaSnapshot(transferId, Guid.NewGuid(), Index, Term,
            ImageLength, checksum, imageFile);
        var vote = Inspect(new VoteRequest(Voter, Term, Index, Term));
        await Assert.That(vote.Metadata(vote.Value.CandidateId, Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        await Assert.That(vote.Value.Term).IsEqualTo(Term);
        await Assert.That(vote.Value.LastIndex).IsEqualTo(Index);
        await Assert.That(vote.Value.LastTerm).IsEqualTo(Term);
        var begin = Inspect(new SnapshotBeginRequest(Voter, Term, snapshot));
        await Assert.That(begin.Metadata(begin.Value.LeaderId, Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        await Assert.That(begin.Value.Term).IsEqualTo(Term);
        await Assert.That(begin.Value.Snapshot.TransferId).IsEqualTo(snapshot.TransferId);
        await Assert.That(begin.Value.Snapshot.Length).IsEqualTo(ImageLength);
        await Assert.That(begin.Metadata(begin.Value.Snapshot.Sha256, Encoding.UTF8.GetByteCount(checksum)))
            .IsEqualTo(checksum);
        await Assert.That(begin.Metadata(begin.Value.Snapshot.FileName, Encoding.UTF8.GetByteCount(imageFile)))
            .IsEqualTo(imageFile);
        var chunk = Inspect(new SnapshotChunkRequest(Voter, Term, snapshot.TransferId, 0, ChunkBytes));
        await Assert.That(chunk.Metadata(chunk.Value.LeaderId, Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        await Assert.That(chunk.Value.Term).IsEqualTo(Term);
        await Assert.That(chunk.Value.TransferId).IsEqualTo(snapshot.TransferId);
        await Assert.That(chunk.Value.Offset).IsEqualTo(0L);
        await Assert.That(chunk.Value.Bytes.Span.SequenceEqual(ChunkBytes)).IsTrue();
        var complete = Inspect(new SnapshotCompleteRequest(Voter, Term, snapshot.TransferId));
        await Assert.That(complete.Metadata(complete.Value.LeaderId, Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        await Assert.That(complete.Value.Term).IsEqualTo(Term);
        await Assert.That(complete.Value.TransferId).IsEqualTo(snapshot.TransferId);
    }

    [Test]
    public async Task AcIs005And008GeneratedStoredStateAndMembershipRetainExactRecordScope()
    {
        var incarnation = Guid.NewGuid();
        var state = Inspect(new ReplicaHardState(ReplicaProtocol.FormatVersion, incarnation, Term, Voter, Index, Index, null));
        await Assert.That(state.Value.Version).IsEqualTo(ReplicaProtocol.FormatVersion);
        await Assert.That(state.Value.Incarnation).IsEqualTo(incarnation);
        await Assert.That(state.Value.Term).IsEqualTo(Term);
        await Assert.That(state.Value.LastIndex).IsEqualTo(Index);
        await Assert.That(state.Value.CommittedIndex).IsEqualTo(Index);
        await Assert.That(state.Value.Snapshot).IsNull();
        await Assert.That(state.Metadata(state.Value.VotedFor!, Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        var membership = Inspect(new ReplicaBenchmarkMembershipRecord(ReplicaProtocol.FormatVersion, incarnation,
            ImmutableArray.Create(Voter, OtherVoter)));
        await Assert.That(membership.Value.Version).IsEqualTo(ReplicaProtocol.FormatVersion);
        await Assert.That(membership.Value.Incarnation).IsEqualTo(incarnation);
        await Assert.That(membership.Value.VoterIds.Length).IsEqualTo(MaximumEntries);
        await Assert.That(membership.Metadata(membership.Value.VoterIds[0], Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        await Assert.That(membership.Metadata(membership.Value.VoterIds[1], Encoding.UTF8.GetByteCount(OtherVoter)))
            .IsEqualTo(OtherVoter);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task AcIs005And007GeneratedNativeOperationAndCommandScopesPreserveSignedFields(bool failure)
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, failure ? InvalidPublicPayload : PublicPayload));
        var inspected = Inspect(operation);
        await Assert.That(inspected.Value.Id).IsEqualTo(operation.Id);
        await Assert.That(inspected.Value.Kind).IsEqualTo(operation.Kind);
        await Assert.That(inspected.Value.EvaluatedAt).IsEqualTo(operation.EvaluatedAt);
        await Assert.That(inspected.Metadata(inspected.Value.PrincipalId, Encoding.UTF8.GetByteCount(Principal))).IsEqualTo(Principal);
        await Assert.That(inspected.Utf8(inspected.Value.PayloadJson).Span.SequenceEqual(Encoding.UTF8.GetBytes(operation.PayloadJson)))
            .IsTrue();
        await Assert.That(inspected.Value.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        var body = ReplicaNativeInspection.InspectNative<NativeCommandPayload>(operation.NativePayload, MaximumEntries);
        await Assert.That(body.Value.Error).IsEqualTo(payload.Error);
        await Assert.That(body.Value.Value.Span.SequenceEqual(payload.Value.Span)).IsTrue();
        await Assert.That(body.Value.Authority.Span.SequenceEqual(payload.Authority.Span)).IsTrue();
        await Assert.That(body.Value.Signature.Span.SequenceEqual(payload.Signature.Span)).IsTrue();
        if (payload.SafeDetail is { } detail)
        {
            await Assert.That(body.Metadata(body.Value.SafeDetail!, Encoding.UTF8.GetByteCount(detail))).IsEqualTo(detail);
        }
        else
        {
            await Assert.That(body.Value.SafeDetail).IsNull();
        }
    }

    [Test]
    public async Task AcIs005GeneratedEntryBatchAndAppendPreserveOperationReferencesAndExactMeasure()
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, PublicPayload));
        ImmutableArray<ReplicaEntry> entries = [new(Index, Term, operation), new(Index + 1, Term, operation)];
        var batch = Inspect(new ReplicaEntryBatch(entries));
        await Assert.That(batch.Value.Entries.Length).IsEqualTo(MaximumEntries);
        await Assert.That(ReferenceEquals(batch.Value.Entries[0].Operation, batch.Value.Entries[1].Operation)).IsTrue();
        await Assert.That(batch.Value.Entries[0].Index).IsEqualTo(Index);
        await Assert.That(batch.Value.Entries[1].Index).IsEqualTo(Index + 1);
        await Assert.That(batch.Value.Entries[0].Term).IsEqualTo(Term);
        await Assert.That(batch.MeasureEntries(batch.Value.Entries)).IsEqualTo(ReplicaProtocolCodec.MeasureEntries(entries));
        var append = Inspect(new AppendRequest(Voter, Term, 0, 0, 0, entries));
        await Assert.That(append.Value.Entries.Length).IsEqualTo(MaximumEntries);
        await Assert.That(ReferenceEquals(append.Value.Entries[0].Operation, append.Value.Entries[1].Operation)).IsTrue();
        await Assert.That(append.MeasureEntries(append.Value.Entries)).IsEqualTo(ReplicaProtocolCodec.MeasureEntries(entries));
    }

    private static ReplicaInspectedValue<T> Inspect<T>(T value)
        => ReplicaNativeInspection.Inspect<T>(ReplicaProtocolCodec.Serialize(value), MaximumEntries);
}
