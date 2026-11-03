using System.Collections.Immutable;
using KeyLoad.Core.AnalyticsStore;
using KeyLoad.FunctionalTests.RelationalStorage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal sealed class NonTransactionalAnalyticsTests
{
    [Test]
    public async Task PlainAnalyticsKeepsNativeDataAndTransactionalOutcomeAcrossColdOpen()
    {
        using var fixture = new NonTransactionalTableFixture();
        var definition = new ResourceDefinition(RelationalTestData.Table, ResourceKind.AnalyticsStore, fixture.Partition.TransactionDomainId)
        {
            Transactional = false,
            AnalyticsSchema = new([], [])
        };
        (await fixture.SubmitAsync(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition))).Get<ResourceDefinition>();
        var timestamp = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        var data = new AnalyticsEventData(RelationalTestData.First, timestamp,
            ImmutableDictionary<string, string>.Empty, ImmutableDictionary<string, double>.Empty, null, []);
        var mutation = new UpsertAnalyticsEvents(definition.Name, [data], ImmutableDictionary<string, long>.Empty.Add(data.Id, 0));
        var first = (await fixture.BatchAsync(id, mutation)).Get<CommitReceipt>();
        await Assert.That((await fixture.BatchAsync(id, mutation)).Get<CommitReceipt>()).IsEquivalentTo(first);
        var key = AnalyticsStoreKeys.Event(fixture.Partition, definition.Name, data.Id);
        var indexKey = AnalyticsStoreKeys.Timestamp(fixture.Partition, definition.Name, timestamp, data.Id);
        var expected = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        fixture.Store.Dispose();
        using (var plain = ZoneTreeTreeFactory.Plain(fixture.Destination(NonTransactionalTableFixture.PlainDirectory)).Open())
        using (var tree = ZoneTreeTreeFactory.Open(new(fixture.Owner.Directory), requireExisting: true))
        using (var snapshot = tree.BeginReadOnlyTransaction())
        {
            await Assert.That(plain.TryGet(key, out var row)).IsTrue();
            await Assert.That(row.Value.ToArray()).IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(snapshot.TryGet(key, out _)).IsFalse();
            await Assert.That(plain.TryGet(indexKey, out var indexed)).IsTrue();
            await Assert.That(OrleansSerialization.Deserialize<string>(indexed.Value.Span)).IsEqualTo(data.Id);
            await Assert.That(snapshot.TryGet(indexKey, out _)).IsFalse();
            await Assert.That(snapshot.TryGet(fixture.OutcomeKey(id), out _)).IsTrue();
        }
        using var cold = NonTransactionalTableFixture.Open(fixture.Owner.Directory);
        var database = fixture.Owner.CreateConfiguredDatabase(cold);
        var page = database.ReadAnalyticsEvents(RelationalTestData.Root,
            new(fixture.Partition, definition.Name, timestamp, timestamp.AddTicks(1), 10, null));
        await Assert.That(page.Events).HasSingleItem();
        await Assert.That(page.Events[0].Id).IsEqualTo(data.Id);
        await Assert.That(page.Events[0].Revision).IsEqualTo(1L);
    }
}
