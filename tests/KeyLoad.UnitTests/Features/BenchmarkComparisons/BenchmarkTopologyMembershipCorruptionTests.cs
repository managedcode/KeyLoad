using System.Buffers.Binary;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004: malformed or incomplete private authority never opens a replica.</summary>
internal sealed class BenchmarkTopologyMembershipCorruptionTests
{
    private const string VersionField = "version";
    private const string IncarnationField = "incarnation";
    private const string VotersField = "voterIds";
    private const string UnknownField = "privateField";
    private const string MalformedJson = "{";
    private const string NullJson = "null";

    [Test]
    [Arguments(MalformedJson)]
    [Arguments(NullJson)]
    public async Task AcIso004InvalidGuardEncodingFailsClosed(string json)
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(3);
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, configuration);
            store.Commit((transaction, _) =>
            {
                var body = System.Text.Encoding.UTF8.GetBytes(json);
                var malformed = new byte[ReplicaProtocol.PayloadPrefixBytes + body.Length];
                BinaryPrimitives.WriteUInt64LittleEndian(malformed, ReplicaProtocol.PayloadMagic);
                body.CopyTo(malformed, ReplicaProtocol.PayloadPrefixBytes);
                transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, malformed);
                return true;
            });
        }
        using var reopened = fixture.Open();
        var failure = BenchmarkTopologyMembershipFixture.Reject(reopened, configuration with { BenchmarkTopology = false });
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    [Arguments(VersionField)]
    [Arguments(VotersField)]
    [Arguments(UnknownField)]
    [Arguments(IncarnationField)]
    public async Task AcIso004InvalidGuardVersionShapeOrAuthorityIsRejected(string field)
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(3);
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, configuration);
            var record = ReplicaProtocolCodec.Deserialize<ReplicaBenchmarkMembershipRecord>(BenchmarkTopologyMembershipFixture.Membership(store)!);
            var malformed = Mutate(record, field);
            store.Commit((transaction, _) =>
            {
                transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, malformed);
                return true;
            });
        }
        using var reopened = fixture.Open();
        var failure = BenchmarkTopologyMembershipFixture.Reject(reopened, configuration);
        await Assert.That(failure.Code).IsEqualTo(field == IncarnationField ? ErrorCode.TokenInvalidated : ErrorCode.Corruption);
        await Assert.That(failure.Message).DoesNotContain(BenchmarkTopologyMembershipFixture.PrivateCanary);
    }

    [Test]
    public async Task AcIso004OrphanMembershipCannotReinitializeHardState()
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(3);
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, configuration);
            store.Commit((transaction, _) => { transaction.Delete(BenchmarkTopologyMembershipFixture.HardStateKey); return true; });
        }
        using var reopened = fixture.Open();
        var position = reopened.Position;
        var failure = BenchmarkTopologyMembershipFixture.Reject(reopened, configuration);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(reopened.Position).IsEqualTo(position);
        await Assert.That(BenchmarkTopologyMembershipFixture.HardState(reopened)).IsNull();
    }

    [Test]
    public async Task AcIso004DuplicateAuthorityFieldsCannotBeAmbiguouslyDecoded()
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(3);
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, configuration);
            var record = ReplicaProtocolCodec.Deserialize<ReplicaBenchmarkMembershipRecord>(BenchmarkTopologyMembershipFixture.Membership(store)!);
            var malformed = BenchmarkTopologyMembershipNativeCodec.Encode(record, BenchmarkMembershipDefect.Duplicate);
            await Assert.That(malformed.AsSpan().SequenceEqual(BenchmarkTopologyMembershipFixture.Membership(store))).IsFalse();
            store.Commit((transaction, _) =>
            {
                transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, malformed);
                return true;
            });
        }
        using var reopened = fixture.Open();
        var failure = BenchmarkTopologyMembershipFixture.Reject(reopened, configuration);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    private static byte[] Mutate(ReplicaBenchmarkMembershipRecord record, string field)
        => field == IncarnationField
            ? ReplicaProtocolCodec.Serialize(record with { Incarnation = Guid.NewGuid() })
            : BenchmarkTopologyMembershipNativeCodec.Encode(record, field switch
            {
                VersionField => BenchmarkMembershipDefect.WrongVersionScalar,
                VotersField => BenchmarkMembershipDefect.Missing,
                _ => BenchmarkMembershipDefect.Unknown
            });
}
