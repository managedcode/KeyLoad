using KeyLoad.Storage;

namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class CanonicalKeyContractTests
{
    private const string SystemSpace = "system";
    private const string AppliedName = "last-applied";
    private const string ClockName = "clock";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string FirstKey = "customer-1";
    private const string SecondKey = "customer-2";

    [Test]
    public async Task AcRoc002CanonicalSystemKeysKeepExactBytesThroughReadOnlyPublicProperties()
    {
        var applied = KeySpace.Applied;
        var clock = KeySpace.Clock;
        var owned = applied.ToArray();
        owned[0] ^= byte.MaxValue;

        await Assert.That(typeof(KeySpace).GetProperty(nameof(KeySpace.Applied))!.PropertyType)
            .IsEqualTo(typeof(ReadOnlyMemory<byte>));
        await Assert.That(typeof(KeySpace).GetProperty(nameof(KeySpace.Clock))!.PropertyType)
            .IsEqualTo(typeof(ReadOnlyMemory<byte>));
        await Assert.That(applied.ToArray()).IsEquivalentTo(KeyCodec.Encode(SystemSpace, AppliedName));
        await Assert.That(clock.ToArray()).IsEquivalentTo(KeyCodec.Encode(SystemSpace, ClockName));
    }

    [Test]
    public async Task AcRoc002AppliedWatermarkUsesTheCanonicalKeyInTheRealStore()
    {
        using var database = new TestDatabase();
        const long position = 7;
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeySpace.Applied.ToArray(), position);
            return true;
        });

        await Assert.That(database.Database.LastApplied).IsEqualTo(position);
    }

    [Test]
    public async Task AcRoc002PartitionCopyComputesItsOwnAtomicIdentity()
    {
        var first = new PartitionRef(Tenant, Database, Domain, FirstKey);
        var original = first.AtomicPartitionId;
        var moved = first with { PartitionKey = SecondKey };
        var unchanged = first with { };

        await Assert.That(moved.AtomicPartitionId)
            .IsEqualTo(new PartitionRef(Tenant, Database, Domain, SecondKey).AtomicPartitionId);
        await Assert.That(moved.AtomicPartitionId).IsNotEqualTo(original);
        await Assert.That(unchanged.AtomicPartitionId).IsEqualTo(original);
        await Assert.That(first.AtomicPartitionId).IsEqualTo(original);
    }
}
