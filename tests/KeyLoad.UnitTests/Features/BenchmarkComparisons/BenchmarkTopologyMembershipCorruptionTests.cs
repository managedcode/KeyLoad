using System.Text.Json.Nodes;
using KeyLoad.Replication;
using KeyLoad.Storage;

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
    private const string VersionProperty = "\"version\":1";
    private const string DuplicateVersionProperty = "\"version\":1,\"version\":1";

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
                transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, System.Text.Encoding.UTF8.GetBytes(json));
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
            var record = JsonNode.Parse(BenchmarkTopologyMembershipFixture.Membership(store)!)!.AsObject();
            Mutate(record, field);
            store.Commit((transaction, _) =>
            {
                transaction.PutRecord(BenchmarkTopologyMembershipFixture.MembershipKey, record);
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
            var json = System.Text.Encoding.UTF8.GetString(BenchmarkTopologyMembershipFixture.Membership(store)!)
                .Replace(VersionProperty, DuplicateVersionProperty, StringComparison.Ordinal);
            await Assert.That(json).Contains(DuplicateVersionProperty);
            store.Commit((transaction, _) =>
            {
                transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, System.Text.Encoding.UTF8.GetBytes(json));
                return true;
            });
        }
        using var reopened = fixture.Open();
        var failure = BenchmarkTopologyMembershipFixture.Reject(reopened, configuration);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    private static void Mutate(JsonObject record, string field)
    {
        if (field == VotersField)
        {
            record.Remove(VotersField);
        }
        else if (field == VersionField)
        {
            record[VersionField] = 2;
        }
        else if (field == IncarnationField)
        {
            record[IncarnationField] = Guid.NewGuid();
        }
        else
        {
            record[UnknownField] = BenchmarkTopologyMembershipFixture.PrivateCanary;
        }
    }
}
