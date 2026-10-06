using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class KurrentStreamOwnershipTests
{
    private const string First = "ownership-first", Second = "ownership-second", Third = "ownership-third";
    private static readonly Guid FirstId = Guid.Parse("a71815da-5fcf-454b-b53b-d4bd28bd1d12");
    private static readonly Guid SecondId = Guid.Parse("13025213-f77e-4821-b2da-eeb6f1a7cd43");

    /// <summary>AC-KO-003: a reservation, rejection or unknown result never grants deletion authority.</summary>
    [Test]
    public async Task OnlyActualAcknowledgementTransitionEntersSelectedSnapshot()
    {
        var ledger = Create();
        ledger.Reserve(First, FirstId);
        ledger.Reserve(Second, SecondId);
        ledger.Reserve(Third, FirstId);
        await Assert.That(ledger.SnapshotAcknowledged().Length).IsEqualTo(0);
        ledger.Reject(Second, SecondId);
        ledger.MarkUnknown(Third, FirstId);
        await Assert.That(ledger.SnapshotAcknowledged().Length).IsEqualTo(0);
        ledger.Acknowledge(First, FirstId);
        await Assert.That(ledger.SnapshotAcknowledged().SequenceEqual([First])).IsTrue();
        await Assert.That(ledger.Count).IsEqualTo(3);
    }

    [Test]
    public async Task DuplicateReservationsAndDifferentDescriptorsFailClosed()
    {
        var ledger = Create();
        ledger.Reserve(First, FirstId);
        await Assert.That(() => ledger.Reserve(First, FirstId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.Reserve(First, SecondId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.Acknowledge(First, SecondId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.Reject(First, SecondId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.MarkUnknown(First, SecondId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.Acknowledge(Second, FirstId)).Throws<ComparisonFailureException>();
        await Assert.That(ledger.Count).IsEqualTo(1);
        await Assert.That(ledger.SnapshotAcknowledged().Length).IsEqualTo(0);
    }

    [Test]
    public async Task TerminalStatesCannotBePromotedReclassifiedOrAcknowledgedTwice()
    {
        var ledger = Create();
        ledger.Reserve(First, FirstId);
        ledger.Reserve(Second, SecondId);
        ledger.Reserve(Third, FirstId);
        ledger.Acknowledge(First, FirstId);
        ledger.Reject(Second, SecondId);
        ledger.MarkUnknown(Third, FirstId);
        await Assert.That(() => ledger.Acknowledge(First, FirstId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.MarkUnknown(First, FirstId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.Acknowledge(Second, SecondId)).Throws<ComparisonFailureException>();
        await Assert.That(() => ledger.Acknowledge(Third, FirstId)).Throws<ComparisonFailureException>();
        await Assert.That(ledger.SnapshotAcknowledged().SequenceEqual([First])).IsTrue();
    }

    /// <summary>AC-KO-004: a later independent unknown outcome cannot erase an earlier ACK.</summary>
    [Test]
    public async Task EarlierAcknowledgementSurvivesLaterFailureAndSnapshotsAreIndependent()
    {
        var ledger = Create();
        ledger.Reserve(First, FirstId);
        ledger.Acknowledge(First, FirstId);
        var snapshot = ledger.SnapshotAcknowledged();
        ledger.Reserve(Second, SecondId);
        ledger.MarkUnknown(Second, SecondId);
        snapshot[0] = Second;
        await Assert.That(ledger.SnapshotAcknowledged().SequenceEqual([First])).IsTrue();
    }

    [Test]
    public async Task ActualProfileCapacityIncludesBothProbesAndFailsBeforeExcessReservation()
    {
        var ledger = Create();
        await Assert.That(ledger.Capacity).IsEqualTo(4);
        foreach (var name in new[] { First, Second, Third, nameof(ActualProfileCapacityIncludesBothProbesAndFailsBeforeExcessReservation) })
        {
            ledger.Reserve(name, FirstId);
        }
        await Assert.That(() => ledger.Reserve(nameof(Create), FirstId)).Throws<ComparisonFailureException>();
        await Assert.That(ledger.Count).IsEqualTo(ledger.Capacity);
        await Assert.That(ledger.SnapshotAcknowledged().Length).IsEqualTo(0);
    }

    [Test]
    public async Task IntensiveAndMaximumValidProfilesStayLazyAndHaveCheckedFiniteCapacity()
    {
        var intensive = new KurrentStreamOwnership(NativeExecutionPolicyFixture.Workload(new ComparisonOptions
        { Documents = 4_096, Repetitions = 5, Warmup = 256, Operations = 10_000 }));
        var maximum = new KurrentStreamOwnership(NativeExecutionPolicyFixture.Workload(new ComparisonOptions
        { Documents = 1_000_000, Repetitions = 20, Warmup = 100_000, Operations = 1_000_000 }));
        await Assert.That(intensive.Capacity).IsEqualTo(55_378);
        await Assert.That(maximum.Capacity).IsEqualTo(23_000_002);
        await Assert.That(intensive.Count).IsEqualTo(0);
        await Assert.That(maximum.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidAndOverflowingProfilesFailValidationBeforeAnyReservation()
    {
        ComparisonOptions[] invalid =
        [
            new() { Documents = 0 }, new() { Warmup = -1 }, new() { Repetitions = 0 },
            new() { Operations = int.MaxValue, Warmup = int.MaxValue, Repetitions = int.MaxValue },
            new() { Documents = int.MaxValue }, new() { Topology = (ComparisonTopology)99 },
        ];
        foreach (var options in invalid)
        {
            await Assert.That(() => { _ = new KurrentStreamOwnership(NativeExecutionPolicyFixture.Workload(options)); }).Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task EmptyDescriptorsAreRejectedWithoutConsumingCapacity()
    {
        var ledger = Create();
        await Assert.That(() => ledger.Reserve(string.Empty, FirstId)).Throws<ArgumentException>();
        await Assert.That(() => ledger.Reserve(First, Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(ledger.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentDistinctOriginalReservationsRetainEveryAcknowledgedCandidate()
    {
        const int workers = 16;
        var ledger = new KurrentStreamOwnership(NativeExecutionPolicyFixture.Workload(new ComparisonOptions
        { Documents = 1, TopK = 1, Operations = workers, Warmup = 0, Repetitions = 1 }));
        var originals = Enumerable.Range(0, workers).Select(index => Task.Run(() =>
        {
            var name = First + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ledger.Reserve(name, FirstId);
            ledger.Acknowledge(name, FirstId);
        })).ToArray();
        await Task.WhenAll(originals);
        var snapshot = ledger.SnapshotAcknowledged();
        await Assert.That(ledger.Count).IsEqualTo(workers);
        await Assert.That(snapshot.Length).IsEqualTo(workers);
        await Assert.That(snapshot.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(workers);
    }

    private static KurrentStreamOwnership Create()
        => new(NativeExecutionPolicyFixture.Workload(new ComparisonOptions { Documents = 1, TopK = 1, Operations = 1, Warmup = 0, Repetitions = 1 }));
}
