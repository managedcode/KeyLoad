using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using ManagedCode.Communication.Commands;
using D = KeyLoad.UnitTests.Features.InternalSerialization.AnalyticsCommandIdentityData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class AnalyticsCommandIdentityAssertions
{
    private const string MutationsKey = "mutations";
    private const string KindKey = "kind";
    private const string JsonKey = "json";
    private const string ExpectedRevisionsKey = "expectedRevisions";
    private const string EventsKey = "events";
    private const string IdKey = "id";
    private const string DimensionsKey = "dimensions";
    private const string MetricsKey = "metrics";

    internal static async Task OrderAsync(Command<DatabaseCommand> operation, bool delete)
    {
        var request = OrleansSerialization.Deserialize<CommandRequest>(operation.Value!.Input.Span);
        using var document = JsonSerializer.SerializeToDocument(request, JsonDefaults.Options);
        await Assert.That(operation.Value.Fingerprint).IsEqualTo(JsonData.Fingerprint(new
        {
            Id = operation.CommandId,
            Kind = operation.Value.Kind,
            PrincipalId = operation.UserId!,
            PayloadJson = D.CanonicalIdentity(request.CommandId, request.Partition, delete)
        }));
        var mutations = document.RootElement.GetProperty(MutationsKey);
        await Assert.That(mutations[0].GetProperty(KindKey).GetString()).IsEqualTo("putDocument");
        await Assert.That(mutations[0].GetProperty(JsonKey).GetString()).IsEqualTo(D.Json);
        var analytics = mutations[1];
        await Assert.That(analytics.GetProperty(KindKey).GetString()).IsEqualTo(delete
            ? AnalyticsStoreProtocol.DeleteKind : AnalyticsStoreProtocol.UpsertKind);
        await KeysAsync(analytics.GetProperty(ExpectedRevisionsKey));
        if (!delete)
        {
            var events = analytics.GetProperty(EventsKey);
            await Assert.That(events[0].GetProperty(IdKey).GetString()).IsEqualTo(D.First);
            await Assert.That(events[1].GetProperty(IdKey).GetString()).IsEqualTo(D.Last);
            foreach (var value in events.EnumerateArray())
            {
                await KeysAsync(value.GetProperty(DimensionsKey));
                await KeysAsync(value.GetProperty(MetricsKey));
            }
        }
    }

    private static async Task KeysAsync(JsonElement map)
        => await Assert.That(map.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual([D.First, D.Last], StringComparer.Ordinal)).IsTrue();

    internal static EntityRef Reference(SafeProjectorOperationDatabase owner) => owner.Store.Read(view =>
    {
        var partition = owner.Source.Partition;
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, D.Collection));
        var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, D.Collection, definition)
            ?? throw new InvalidOperationException("The configured identity-test collection lifetime is missing.");
        return new EntityRef(partition, D.Collection, D.Document) { Lifetime = new(lifetime.Incarnation.Value) };
    });

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await O.EqualAsync(expected, actual);
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences.IsEmpty).IsTrue();
        }
    }

    internal static async Task RowsAsync(SafeProjectorOperationDatabase owner, bool delete, EntityRef expectedReference, CancellationToken token)
    {
        var expected = delete ? ImmutableArray<AnalyticsEventRecord>.Empty : new[] { D.First, D.Last }
            .Select(id => D.Event(id, false)).Select(value => new AnalyticsEventRecord(value.Id, value.Timestamp,
                value.Dimensions, value.Metrics, null, [], 1)).ToImmutableArray();
        await O.EqualAsync(new AnalyticsEventsPage(expected, false, null), owner.Database.ReadAnalyticsEvents(D.Root,
            new(owner.Source.Partition, D.Store, D.From, D.From.AddHours(1), 10, null), token));
        var row = owner.Database.GetDocument(D.Root, expectedReference, cancellationToken: token);
        await O.EqualAsync(new DocumentResult(expectedReference, 1, D.Json, false, []), row);
    }

    internal static async Task UnchangedAsync(SafeProjectorOperationDatabase owner, Func<Task> action)
    {
        var identity = OrleansSerialization.Serialize(owner.Store.Identity);
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var position = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var cut = owner.ReadCanonicalCut();
        var outbox = owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition);
        await action();
        await Assert.That(OrleansSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(cut);
        await O.EqualAsync(outbox, owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition));
    }

    internal static async Task NonAnalyticsAsync()
    {
        using var database = new TestDatabase();
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition, [new PutDocument(D.Collection, D.Document, D.Json, 0)]);
        await RuntimeIdentityAsync(database.Database, OperationKind.Batch, id, request);
        var definition = new ResourceDefinition(D.Collection, ResourceKind.Collection, database.Partition.TransactionDomainId);
        await RuntimeIdentityAsync(database.Database, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition));
    }

    private static async Task RuntimeIdentityAsync<T>(DatabaseEngine database, OperationKind kind, Guid id, T request)
    {
        var identity = JsonSerializer.Serialize(request, JsonDefaults.Options);
        var operation = database.CreateCommand(kind, id, D.Root, database.EvaluationClock.GetUtcNow(), identity);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock,
            TestContext.Current!.Execution.CancellationToken);
        var charged = database.CreateCommand(kind, id, D.Root, new DateTimeOffset(operation.Timestamp), OrleansSerialization.Serialize(request), work);
        await Assert.That(charged.Value!.Input.Span.SequenceEqual(operation.Value!.Input.Span)).IsTrue();
        await Assert.That(charged.Value.Fingerprint).IsEqualTo(operation.Value.Fingerprint);
        await Assert.That(DatabaseCommandFingerprint.Compute(operation)).IsEqualTo(JsonData.Fingerprint(new
        {
            Id = operation.CommandId,
            Kind = operation.Value.Kind,
            PrincipalId = operation.UserId!,
            PayloadJson = identity
        }));
    }
}
