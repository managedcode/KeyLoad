using System.Globalization;
using KeyLoad;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class NativeSagaTimeoutJobContractTests
{
    [Test]
    public async Task MetadataRoundTripsOnlyTheBoundedCanonicalHint()
    {
        var hint = new DueWorkHint(DueWorkKind.Saga,
            new QueueLaneRef(new PartitionRef("tenant", "database", "orders", "partition-1"), "timeouts"),
            Guid.Parse("f298d10a-8993-4eef-a5b5-804c8bf3453f"), "creator", 7, 0, 0,
            new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var metadata = NativeSagaTimeoutJobContract.Create(hint);
        var actual = NativeSagaTimeoutJobContract.Parse(metadata);

        await Assert.That(metadata.Count).IsEqualTo(11);
        await Assert.That(actual).IsEqualTo(hint);
        await Assert.That(metadata.ContainsKey("payload")).IsFalse();
        await Assert.That(metadata.ContainsKey("state")).IsFalse();
    }

    [Test]
    public async Task MetadataRejectsUnknownKeysAndInvalidCanonicalValues()
    {
        var metadata = NativeSagaTimeoutJobContract.Create(new DueWorkHint(DueWorkKind.Saga,
            new QueueLaneRef(new PartitionRef("tenant", "database", "orders", "partition-1"), "timeouts"),
            Guid.NewGuid(), "creator", 3, 0, 0, DateTimeOffset.UtcNow));

        var extra = new Dictionary<string, string>(metadata, StringComparer.Ordinal) { ["extra"] = "value" };
        var invalidRevision = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            [NativeSagaTimeoutJobContract.RevisionKey] = "-1"
        };
        var invalidDeadline = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            [NativeSagaTimeoutJobContract.DeadlineKey] = DateTimeOffset.UtcNow
                .ToOffset(TimeSpan.FromHours(1)).ToString("O", CultureInfo.InvariantCulture)
        };

        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => NativeSagaTimeoutJobContract.Parse(extra)));
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => NativeSagaTimeoutJobContract.Parse(invalidRevision)));
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => NativeSagaTimeoutJobContract.Parse(invalidDeadline)));
    }
}
