using KeyLoad.Core;
using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.Authorization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsEntityLinkLifetimeAssertions
{
    internal static Task EqualAsync<T>(T expected, T actual) => O.EqualAsync(expected, actual);

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await EqualAsync(expected, actual);
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences.IsEmpty).IsTrue();
        }
    }

    internal static async Task RowsAsync(AnalyticsEntityLinkLifetimeFixture fixture, ReplicatedOperation operation,
        long revision, CancellationToken token)
    {
        var owner = fixture.Owner;
        var value = D.Event(owner.Source, D.First, D.Alpha, 2) with
        {
            Links = [fixture.Bound]
        };
        var expected = new AnalyticsEventRecord(value.Id, value.Timestamp, value.Dimensions, value.Metrics, null, [fixture.Bound], revision);
        var raw = owner.Store.Read(view => view.ReadOwnedValue(AnalyticsStoreKeys.Event(owner.Source.Partition, D.Store, D.First)))
            ?? throw new InvalidOperationException("The committed analytics event is absent.");
        await EqualAsync(new AnalyticsStoredEvent(expected, false, operation.EvaluatedAt),
            NativeSerialization.Deserialize<AnalyticsStoredEvent>(raw));
        var generation = owner.Store.Read(view => view.ReadOwnedValue(AnalyticsStoreKeys.Generation(owner.Source.Partition, D.Store)))!;
        await Assert.That(NativeSerialization.Deserialize<long>(generation)).IsEqualTo(revision);
        await EqualAsync(new AnalyticsEventsPage([expected], false, null), owner.Database.ReadAnalyticsEvents(D.Root, D.Read(owner.Source), token));
        await EqualAsync(new DocumentResult(fixture.Bound, 1, D.AnchorJson, false, []),
            owner.Database.GetDocument(D.Root, fixture.Bound, cancellationToken: token));
    }

    internal static string[] BusinessImage(AnalyticsEntityLinkLifetimeFixture fixture) => fixture.Owner.Store.Read(view =>
    {
        var partition = fixture.Owner.Source.Partition;
        var prefixes = new List<byte[]> { AnalyticsStoreKeys.Events(partition, D.Store), AnalyticsStoreKeys.Generation(partition, D.Store),
            DocumentStorageKeys.RecordKey(partition, D.AnchorCollection, D.AnchorId),
            DocumentStorageKeys.RecordKey(partition, AnalyticsEntityLinkLifetimeFixture.Other, D.AnchorId),
            KeySpace.Partition("outbox", partition), KeySpace.Partition("outbox-head", partition) };
        foreach (var name in new[] { D.Store, D.AnchorCollection, AnalyticsEntityLinkLifetimeFixture.Other })
        {
            prefixes.Add(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name));
            prefixes.Add(ResourceLifetimeCatalog.Key(partition.TenantId, partition.DatabaseId, name));
        }
        var rows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prefix in prefixes)
        {
            var page = view.Scan(prefix, 64);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The bounded link fixture business image is incomplete.");
            }

            foreach (var row in page.Records)
            {
                rows.Add(Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
            }
        }
        return rows.Order(StringComparer.Ordinal).ToArray();
    });

    internal static async Task UnchangedAsync(AnalyticsEntityLinkLifetimeFixture fixture, Func<Task> read)
    {
        var owner = fixture.Owner;
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var identity = NativeSerialization.Serialize(owner.Store.Identity);
        var position = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var journal = owner.JournalCut();
        await read();
        await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(owner.JournalCut()).IsEqualTo(journal);
    }
}
