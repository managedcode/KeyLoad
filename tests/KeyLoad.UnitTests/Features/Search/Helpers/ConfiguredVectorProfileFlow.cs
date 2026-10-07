using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ConfiguredVectorProfileFlow
{
    internal const string Collection = "configured-vectors";
    internal const string Source = "source";
    internal const string Target = "target";
    internal const string Rival = "rival";
    internal const string StreamSet = "vector-source-events";
    internal const string Stream = "source-stream";
    internal const string Event = "source-event";
    internal const string Field = "/embedding";
    internal const string Input = "/text";
    internal const string OriginalJson = """{"text":"original"}""";
    internal const string UpdatedJson = """{"text":"updated"}""";
    internal const string Mismatch = "The declared vector profile does not match the configured field.";
    internal static readonly VectorSpace Space = new(SpaceId, Dimension, DistanceMetric.DotProduct, ModelId, ModelVersion);
    private const string Root = "root";
    private const string VectorKeySpace = "vector";
    private const int Dimension = 2;
    private const string SpaceId = "configured-space";
    private const string ModelId = "trusted-model";
    private const string ModelVersion = "1";
    private const string WrongModel = "other-valid-model";
    private const string Reducer = "configured-reducer";
    private const string Version = "1";
    private const long InitialRevision = 1;
    private const long UpdatedRevision = 2;
    private const int ScanBound = 4_096;
    private const int CommitAdvance = 1;
    private const double FirstRank = 1d / 61;
    private const double SecondRank = 1d / 62;

    internal static void Seed(TestDatabase database)
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, database.Partition.TransactionDomainId)
        { VectorProfiles = [new(Field, Space)] };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        database.Configure(StreamSet, ResourceKind.StreamSet);
        database.Commit(new PutDocument(Collection, Source, OriginalJson), new PutDocument(Collection, Target, OriginalJson),
            new PutDocument(Collection, Rival, OriginalJson), new PutVector(Collection, Target, Field, [1, 0], Space, InitialRevision),
            new PutVector(Collection, Rival, Field, [0.5f, 0.5f], Space, InitialRevision),
            new AppendEvents(StreamSet, Stream, [new(Event, "Created", "{}")], ExpectedStreamRevision.NoStream));
    }
    internal static CommandRequest Command(TestDatabase database, bool projection, bool wrongModel)
    {
        var vector = new PutVector(Collection, Target, Field, [0, 1],
            wrongModel ? Space with { Model = WrongModel } : Space, UpdatedRevision);
        Mutation effect = projection ? new ApplyVectorProjection(new(database.Partition, StreamSet, Stream, InitialRevision),
            InitialRevision, Event, new(database.Partition, Collection, Source), InitialRevision, Input,
            Reducer, Version, InitialRevision, vector) : vector;
        return new(Guid.NewGuid(), database.Partition, [new PutDocument(Collection, Target, UpdatedJson, InitialRevision), effect]);
    }
    internal static async Task RejectAsync(TestDatabase database, CommandRequest command)
    {
        var model = ModelBytes(database, command);
        var position = database.Store.Position;
        var rejected = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(rejected.SafeDetail).IsEqualTo(Mismatch);
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position + CommitAdvance);
        await Assert.That(ModelBytes(database, command)).IsEquivalentTo(model, CollectionOrdering.Matching);
        var complete = QueueWholeFlowStorage.Bytes(database.Store);
        var replay = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(rejected))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position + CommitAdvance);
    }
    internal static string[] ModelBytes(TestDatabase database, CommandRequest command, string principal = Root) =>
        StateBytes(database, command.CommandId, principal);

    internal static string[] StateBytes(TestDatabase database, Guid commandId, string principal = Root, bool global = false) => database.Store.Read(view =>
    {
        var page = view.Scan([], ScanBound);
        if (page.HasMore)
        { throw new InvalidOperationException("The configured vector fixture exceeded its complete-store bound."); }
        var outcome = global ? OutcomeStoreOracle.GlobalKey(principal, commandId)
            : OutcomeStoreOracle.PartitionKey(database.Partition, principal, commandId);
        var locator = global ? null : KeySpace.OutcomeLocatorV2(database.Partition, principal, commandId);
        return page.Records.Where(record => !record.Key.Span.SequenceEqual(outcome)
            && (locator is null || !record.Key.Span.SequenceEqual(locator))
            && !record.Key.Span.SequenceEqual(KeySpace.ClockBytes)).Select(record => Convert.ToHexString(record.Key.Span)
                + ":" + Convert.ToHexString(record.Value.Span)).ToArray();
    });
    internal static async Task HealthyAsync(DatabaseEngine owner, PartitionRef partition, CancellationToken token)
    {
        var rows = await new SearchEngine(owner, UnitExecutionOptions.QueryExecution()).SearchAsync(Root,
            new(partition, Collection, VectorField: Field, Vector: [1, 0], Space: Space), token);
        await Assert.That(rows.Select(row => row.Document.Reference.Id)).IsEquivalentTo(new[] { Rival, Target }, CollectionOrdering.Matching);
        await Assert.That(rows.Select(row => row.Score)).IsEquivalentTo(new[] { FirstRank, SecondRank }, CollectionOrdering.Matching);
        var target = rows.Single(row => row.Document.Reference.Id == Target).Document;
        await Assert.That(JsonDefaults.Serialize(target).SequenceEqual(JsonDefaults.Serialize(
            new DocumentResult(new(partition, Collection, Target), UpdatedRevision, UpdatedJson, false, [])))).IsTrue();
        var vector = owner.Store.Read(view => view.GetRecord<VectorRecord>(
            KeySpace.Partition(VectorKeySpace, partition, Collection, Field, Target)));
        var expectedVector = new VectorRecord(Target, Field, Space, [0, 1], UpdatedRevision);
        await Assert.That(NativeSerialization.Serialize(vector!).SequenceEqual(NativeSerialization.Serialize(expectedVector))).IsTrue();
        var rival = rows.Single(row => row.Document.Reference.Id == Rival).Document;
        await Assert.That(JsonDefaults.Serialize(rival).SequenceEqual(JsonDefaults.Serialize(
            new DocumentResult(new(partition, Collection, Rival), InitialRevision, OriginalJson, false, [])))).IsTrue();
        var source = owner.GetDocument(Root, new(partition, Collection, Source), cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(source).SequenceEqual(JsonDefaults.Serialize(
            new DocumentResult(new(partition, Collection, Source), InitialRevision, OriginalJson, false, [])))).IsTrue();
    }
}
