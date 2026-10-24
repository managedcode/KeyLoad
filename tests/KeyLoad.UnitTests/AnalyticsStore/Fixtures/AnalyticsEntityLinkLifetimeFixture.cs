using KeyLoad.Core.AnalyticsStore;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Authorization;
using A = KeyLoad.UnitTests.AnalyticsStore.AnalyticsEntityLinkLifetimeAssertions;
using D = KeyLoad.UnitTests.AnalyticsStore.AnalyticsStoreTestData;

namespace KeyLoad.UnitTests.AnalyticsStore;

internal sealed class AnalyticsEntityLinkLifetimeFixture(SafeProjectorOperationDatabase owner)
{
    internal const string Other = "analytics-other-anchors";
    internal SafeProjectorOperationDatabase Owner => owner;
    internal EntityRef Plain => new(owner.Source.Partition, D.AnchorCollection, D.AnchorId);
    internal EntityRef Bound { get; private set; } = null!;
    internal EntityRef Foreign { get; private set; } = null!;

    internal static Task RunAsync(Func<AnalyticsEntityLinkLifetimeFixture, Task> body, CancellationToken token)
        => SafeProjectorOperationDatabase.RunAsync(async owner =>
        {
            await AnalyticsStoreTestOperations.InitializeAsync(owner.Source, false);
            owner.Source.Configure(Other, ResourceKind.Collection);
            owner.Source.Commit(new PutDocument(Other, D.AnchorId, D.AnchorJson, 0));
            var fixture = new AnalyticsEntityLinkLifetimeFixture(owner);
            fixture.Bound = fixture.Reference(D.AnchorCollection);
            var other = fixture.Reference(Other);
            await Assert.That(other.Lifetime!.Incarnation == fixture.Bound.Lifetime!.Incarnation).IsFalse();
            // Real second-resource incarnation used only as an invalid guard input, never a retired-life proof.
            fixture.Foreign = fixture.Bound with
            {
                Lifetime = other.Lifetime
            };
            foreach (var expected in new[] { fixture.Bound, other })
            {
                var record = owner.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(expected)))!;
                await A.EqualAsync(expected, record.Reference);
                await A.EqualAsync(new DocumentResult(expected, 1, D.AnchorJson, false, []),
                    owner.Database.GetDocument(D.Root, expected, cancellationToken: token));
            }
            await body(fixture);
        }, token);

    private EntityRef Reference(string collection) => owner.Store.Read(view =>
    {
        var partition = owner.Source.Partition;
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection));
        var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, collection, definition)
            ?? throw new InvalidOperationException("The execution anchor resource lifetime is absent.");
        return new EntityRef(partition, collection, D.AnchorId) { Lifetime = new(lifetime.Incarnation.Value) };
    });

    internal async Task CorruptStoredLinkAsync(bool foreign, Func<Task> read)
    {
        var key = AnalyticsStoreKeys.Event(owner.Source.Partition, D.Store, D.First);
        var original = owner.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The committed analytics event is absent.");
        var stored = OrleansSerialization.Deserialize<AnalyticsStoredEvent>(original);
        var damaged = stored with
        {
            Event = stored.Event with
            {
                Links = [foreign ? Foreign : Plain]
            }
        };
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var identity = OrleansSerialization.Serialize(owner.Store.Identity);
        var position = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var journal = owner.ReadCanonicalCut();
        var failures = new List<Exception>();
        // Deliberate current-format row corruption, using the existing owned execution-store fault/restore discipline.
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owner.Store.Commit((transaction, _) => { transaction.PutRecord(key, damaged); return true; });
            await read();
        }, failures);
        ServerFailureObserver.Observe(() => owner.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; }), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await Assert.That(OrleansSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
            await Assert.That(owner.Store.Position).IsEqualTo(position + 2);
            await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
            await Assert.That(owner.ReadCanonicalCut()).IsEqualTo((journal.AppliedPosition, journal.StorePosition + 2));
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
