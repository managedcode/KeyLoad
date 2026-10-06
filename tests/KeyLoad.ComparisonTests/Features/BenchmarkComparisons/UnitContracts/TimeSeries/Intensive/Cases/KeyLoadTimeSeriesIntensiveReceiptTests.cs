using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class KeyLoadTimeSeriesIntensiveReceiptTests
{
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid CommandId = Guid.Parse("10112233-4455-6677-8899-aabbccddeeff");
    private const string Set = "metrics";
    private const string Series = "series-a";
    private const string PartitionKey = "partition";

    [Test]
    public async Task AcTsi002NonOrdinalActualRevisionIsTheReturnedSequence()
    {
        var receipt = ValidReceipt(revision: 731, position: 908);
        var actual = KeyLoadTimeSeriesIntensiveReceipt.Validate(receipt, CommandId, Incarnation,
            Partition().AtomicPartitionId, Set, Series, expectedRevision: null);
        await Assert.That(actual.Receipt.CommandId).IsEqualTo(CommandId);
        await Assert.That(actual.Receipt.Sequence).IsEqualTo(731L);
        await Assert.That(actual.Position).IsEqualTo(908L);
    }

    [Test]
    public async Task AcTsi002SeedReceiptMustMatchItsActualTerminalRevision()
    {
        var receipt = ValidReceipt(revision: 512, position: 1024);
        var actual = KeyLoadTimeSeriesIntensiveReceipt.Validate(receipt, CommandId, Incarnation,
            Partition().AtomicPartitionId, Set, Series, expectedRevision: 512);
        await Assert.That(actual.Receipt.Sequence).IsEqualTo(512L);
        var error = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveReceipt.Validate(receipt, CommandId, Incarnation,
                Partition().AtomicPartitionId, Set, Series, expectedRevision: 256));
        await Assert.That(error.ObservedSequence).IsEqualTo(512L);
    }

    [Test]
    public async Task AcTsi002MalformedAuthorityRetainsOnlyUnambiguousMatchingRevision()
    {
        var receipt = ValidReceipt(revision: 29, position: 4) with
        {
            Token = new(Guid.NewGuid(), "wrong-partition", 4, 2)
        };
        var error = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveReceipt.Validate(receipt, CommandId, Incarnation,
                Partition().AtomicPartitionId, Set, Series, expectedRevision: null));
        await Assert.That(error.ObservedSequence).IsEqualTo(29L);
    }

    [Test]
    public async Task AcTsi002MalformedMutationShapeHasNoObservedSequence()
    {
        var valid = ValidReceipt(29, 4);
        var cases = new CommitReceipt?[]
        {
            null,
            valid with { Mutations = default },
            valid with { Mutations = [] },
            valid with { Mutations = [null!] },
            valid with { Mutations = [valid.Mutations[0], valid.Mutations[0]] },
            valid with { Mutations = [valid.Mutations[0], valid.Mutations[0] with { Id = "unrelated" }] },
            valid with { Mutations = [valid.Mutations[0] with { Id = "other-series" }] },
            valid with { Mutations = [valid.Mutations[0] with { Kind = "other-kind" }] },
            valid with { Mutations = [valid.Mutations[0] with { Resource = "other-set" }] },
            valid with { Mutations = [valid.Mutations[0] with { Revision = 0 }] }
        };

        foreach (var candidate in cases)
        {
            var error = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
                KeyLoadTimeSeriesIntensiveReceipt.Validate(candidate, CommandId, Incarnation,
                    Partition().AtomicPartitionId, Set, Series, expectedRevision: null));
            await Assert.That(error.ObservedSequence).IsNull();
        }
    }

    [Test]
    public async Task AcTsi002AuthorityFailuresRetainSoleActualMatchingRevision()
    {
        var valid = ValidReceipt(29, 4);
        var cases = new CommitReceipt?[]
        {
            valid with { CommandId = Guid.NewGuid() },
            valid with { Token = null! },
            valid with { Token = valid.Token with { Incarnation = Guid.NewGuid() } },
            valid with { Token = valid.Token with { AtomicPartitionId = "wrong-partition" } },
            valid with { Token = valid.Token with { OwnershipEpoch = 2 } },
            valid with { Token = valid.Token with { Position = 0 } },
            valid with { Durability = DurabilityProfile.ProcessDurable }
        };

        foreach (var candidate in cases)
        {
            var error = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
                KeyLoadTimeSeriesIntensiveReceipt.Validate(candidate, CommandId, Incarnation,
                    Partition().AtomicPartitionId, Set, Series, expectedRevision: null));
            await Assert.That(error.ObservedSequence).IsEqualTo(29L);
        }
    }

    private static CommitReceipt ValidReceipt(long revision, long position) => new(CommandId,
        new(Incarnation, Partition().AtomicPartitionId, position, KeyLoadTimeSeriesIntensiveProtocol.OwnershipEpoch),
        [new(KeyLoadTimeSeriesIntensiveProtocol.AppendSamplesKind, Set, Series, revision)],
        DurabilityProfile.QuorumProcessDurable);

    private static PartitionRef Partition() => new("tenant", "database", "domain", PartitionKey);
}
