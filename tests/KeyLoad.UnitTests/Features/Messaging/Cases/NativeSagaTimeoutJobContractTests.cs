using System.Globalization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class NativeSagaTimeoutJobContractTests
{
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string TransactionDomain = "orders";
    private const string PartitionKey = "partition-1";
    private const string Queue = "timeouts";
    private const string Creator = "creator";
    private const string PayloadKey = "payload";
    private const string StateKey = "state";
    private const string ExtraKey = "extra";
    private const string TestValue = "value";
    private const string UtcTimestampFormat = "O";
    private const int ExpectedMetadataFieldCount = 11;
    private const int SagaRevision = 7;
    private const int InvalidRevision = -1;
    private const int NonUtcOffsetHours = 1;
    private const int CanonicalYear = 2035;
    private const int CanonicalMonth = 1;
    private const int CanonicalDay = 1;
    private const string SagaIdentifier = "f298d10a-8993-4eef-a5b5-804c8bf3453f";
    private static readonly DateTimeOffset CanonicalDeadline = new(CanonicalYear, CanonicalMonth,
        CanonicalDay, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task MetadataRoundTripsOnlyTheBoundedCanonicalHint()
    {
        var hint = new DueWorkHint(DueWorkKind.Saga,
            new QueueLaneRef(new PartitionRef(Tenant, Database, TransactionDomain, PartitionKey), Queue),
            Guid.Parse(SagaIdentifier), Creator, SagaRevision, DueCoordinatorFields.NoGeneration,
            DueCoordinatorFields.FirstOrdinal, CanonicalDeadline);

        var metadata = NativeSagaTimeoutJobContract.Create(hint);
        var actual = NativeSagaTimeoutJobContract.Parse(metadata);

        await Assert.That(metadata.Count).IsEqualTo(ExpectedMetadataFieldCount);
        await Assert.That(actual).IsEqualTo(hint);
        await Assert.That(metadata.ContainsKey(PayloadKey)).IsFalse();
        await Assert.That(metadata.ContainsKey(StateKey)).IsFalse();
    }

    [Test]
    public async Task MetadataRejectsUnknownKeysAndInvalidCanonicalValues()
    {
        var metadata = NativeSagaTimeoutJobContract.Create(new DueWorkHint(DueWorkKind.Saga,
            new QueueLaneRef(new PartitionRef(Tenant, Database, TransactionDomain, PartitionKey), Queue),
            Guid.NewGuid(), Creator, SagaRevision, DueCoordinatorFields.NoGeneration,
            DueCoordinatorFields.FirstOrdinal, TimeProvider.System.GetUtcNow()));

        var extra = new Dictionary<string, string>(metadata, StringComparer.Ordinal) { [ExtraKey] = TestValue };
        var invalidRevision = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            [NativeSagaTimeoutJobContract.RevisionKey] = InvalidRevision.ToString(CultureInfo.InvariantCulture)
        };
        var invalidDeadline = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            [NativeSagaTimeoutJobContract.DeadlineKey] = TimeProvider.System.GetUtcNow()
                .ToOffset(TimeSpan.FromHours(NonUtcOffsetHours)).ToString(UtcTimestampFormat, CultureInfo.InvariantCulture)
        };

        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => NativeSagaTimeoutJobContract.Parse(extra)));
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => NativeSagaTimeoutJobContract.Parse(invalidRevision)));
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => NativeSagaTimeoutJobContract.Parse(invalidDeadline)));
    }
}
