using System.Buffers.Binary;
using System.Text;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class BenchmarkMembershipUnknownMetadataNoEffectsTests
{
    private const string OriginalNull = "null";
    private const string PrivateMarker = "private-unknown-membership-marker";
    private const int VoterCount = 3;

    [Test]
    public async Task AcNht003UnknownIdDenialsAfterReopenPreservePersistedAuthorityAndPosition()
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(VoterCount);
        var (membership, hardState, initialPosition) = await AssertValidPersistedControl(fixture, configuration);

        var malformed = UnknownIdPayload();
        using (var writer = fixture.Open())
        {
            writer.Commit((transaction, _) =>
            {
                transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, malformed);
                return true;
            });
        }

        using var reopened = fixture.Open();
        var storedMembership = BenchmarkTopologyMembershipFixture.Membership(reopened)!;
        var storedHardState = BenchmarkTopologyMembershipFixture.HardState(reopened)!;
        var cut = reopened.Position;
        await Assert.That(storedMembership.AsSpan().SequenceEqual(malformed)).IsTrue();
        await Assert.That(storedMembership.AsSpan().SequenceEqual(membership)).IsFalse();
        await Assert.That(storedHardState.AsSpan().SequenceEqual(hardState)).IsTrue();
        await Assert.That(cut).IsEqualTo(initialPosition + 1);
        await Assert.That(storedMembership.AsSpan().IndexOf(Encoding.UTF8.GetBytes(PrivateMarker)) >= 0).IsTrue();
        await AssertRejectedWithoutEffects(reopened, configuration, cut, storedMembership, storedHardState);
        await AssertRejectedWithoutEffects(reopened, configuration, cut, storedMembership, storedHardState);
    }

    private static async Task<(byte[] Membership, byte[] HardState, long Position)> AssertValidPersistedControl(
        BenchmarkTopologyMembershipFixture fixture, ReplicaConfiguration configuration)
    {
        byte[] membership;
        byte[] hardState;
        long position;
        using (var store = fixture.Open())
        {
            using (var log = new DurableReplicaLog(store, configuration))
            {
                await Assert.That(log.State.Incarnation).IsEqualTo(fixture.Incarnation);
                await Assert.That(log.State.Term).IsEqualTo(0L);
                await Assert.That(log.State.VotedFor).IsNull();
                var record = ReplicaProtocolCodec.Deserialize<ReplicaBenchmarkMembershipRecord>(
                    BenchmarkTopologyMembershipFixture.Membership(store)!);
                await Assert.That(record.Version).IsEqualTo(ReplicaProtocol.FormatVersion);
                await Assert.That(record.Incarnation).IsEqualTo(fixture.Incarnation);
                await Assert.That(record.VoterIds.AsSpan().SequenceEqual(configuration.VoterIds.AsSpan())).IsTrue();
            }
            membership = BenchmarkTopologyMembershipFixture.Membership(store)!;
            hardState = BenchmarkTopologyMembershipFixture.HardState(store)!;
            position = store.Position;
        }
        using var reopened = fixture.Open();
        using var recovered = new DurableReplicaLog(reopened, configuration);
        await Assert.That(reopened.Position).IsEqualTo(position);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(reopened)!.AsSpan().SequenceEqual(membership)).IsTrue();
        await Assert.That(BenchmarkTopologyMembershipFixture.HardState(reopened)!.AsSpan().SequenceEqual(hardState)).IsTrue();
        await Assert.That(recovered.State.Incarnation).IsEqualTo(fixture.Incarnation);
        return (membership, hardState, position);
    }

    private static byte[] UnknownIdPayload()
    {
        var body = Encoding.UTF8.GetBytes(OriginalNull + PrivateMarker);
        var payload = new byte[ReplicaProtocol.PayloadPrefixBytes + body.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, ReplicaProtocol.PayloadMagic);
        body.CopyTo(payload.AsSpan(ReplicaProtocol.PayloadPrefixBytes));
        return payload;
    }

    private static async Task AssertRejectedWithoutEffects(ZoneTreeStore store, ReplicaConfiguration configuration,
        long position, byte[] membership, byte[] hardState)
    {
        var failure = BenchmarkTopologyMembershipFixture.Reject(store, configuration);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).DoesNotContain(PrivateMarker);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(store)!.AsSpan().SequenceEqual(membership)).IsTrue();
        await Assert.That(BenchmarkTopologyMembershipFixture.HardState(store)!.AsSpan().SequenceEqual(hardState)).IsTrue();
    }
}
