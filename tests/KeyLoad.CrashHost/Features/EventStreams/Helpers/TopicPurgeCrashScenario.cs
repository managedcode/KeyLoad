using KeyLoad.Core;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class TopicPurgeCrashScenario
{
    private const long PositionStep = 1;
    private const long InitialDocumentRevision = 0;
    private const int FirstPublishedPosition = 1;
    private const int SecondPublishedPosition = 2;
    private const int ThirdPublishedPosition = 3;
    private const int InitialEventReadLimit = 1;
    private const long SourceGeneration = 1;
    private const string RetainedEntity = "retained";
    private const string InvalidOutcome = "The seeded purge operation violated its native outcome contract.";
    private const string TruncatedInventory = "The purge fixture inventory exceeded its native bound.";
    private const string RecordSeparator = ":";
    private const string DocumentSpace = "document";
    private const string MessageMetadataSpace = "message-meta";
    private const string MessageBodySpace = "message-body";
    private const string QueueCountersSpace = "queue-counters";
    private const string ReadySpace = "ready";
    private const string SubscriptionSpace = "subscription";
    private const string SubscriptionWindowSpace = "subscription-window";
    private const string SubscriptionCompletionSpace = "subscription-completion";
    private const string SubscriptionInboxSpace = "subscription-inbox";
    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, bool pinned)
    {
        var database = CrashDatabase.Create(store);
        Configure(database);
        var publication = new CommandRequest(TopicPurgeCrashContract.PublishId, TopicPurgeCrashContract.Partition,
            [new PutDocument(TopicPurgeCrashContract.Documents, RetainedEntity, TopicPurgeCrashContract.DocumentJson, InitialDocumentRevision),
             new EnqueueMessage(TopicPurgeCrashContract.Queue, RetainedEntity, TopicPurgeCrashContract.QueueJson, TopicPurgeCrashContract.HeadersJson),
             new PublishTopic(TopicPurgeCrashContract.Topic, [TopicPurgeCrashContract.Event(FirstPublishedPosition), TopicPurgeCrashContract.Event(SecondPublishedPosition), TopicPurgeCrashContract.Event(ThirdPublishedPosition)])]);
        var publish = CrashDatabase.Operation(OperationKind.Batch, publication, publication.CommandId);
        var publishResult = database.Apply(publish);
        publishResult.Get<CommitReceipt>();
        var cursor = database.ReadEventSource(CrashFixtureValues.Principal, new(TopicPurgeCrashContract.Source, Limit: InitialEventReadLimit)).Cursor;
        var seed = CrashDatabase.Operation(OperationKind.Batch, TopicPurgeCrashContract.Purge(TopicPurgeCrashContract.SeedId, FirstPublishedPosition), TopicPurgeCrashContract.SeedId);
        var seedResult = database.Apply(seed);
        seedResult.Get<CommitReceipt>();
        ConfigurePin(database, pinned);
        var operation = CrashDatabase.Operation(OperationKind.Batch, TopicPurgeCrashContract.Purge(TopicPurgeCrashContract.PurgeId, SecondPublishedPosition), TopicPurgeCrashContract.PurgeId);
        await SaveAsync(directory, publish, publishResult, seed, seedResult, operation, store, pinned, cursor);
        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
        var result = database.Apply(operation);
        if (pinned ? result.Error != ErrorCode.ResourceExhausted : result.Error is not null)
        { throw new InvalidOperationException(InvalidOutcome); }
        await CrashHostPause.WaitForKillAsync();
    }
    private static void Configure(DatabaseEngine database)
    {
        foreach (var resource in new[] { new ResourceDefinition(TopicPurgeCrashContract.Documents, ResourceKind.Collection, TopicPurgeCrashContract.Partition.TransactionDomainId),
            new(TopicPurgeCrashContract.Queue, ResourceKind.WorkQueue, TopicPurgeCrashContract.Partition.TransactionDomainId),
            new(TopicPurgeCrashContract.Topic, ResourceKind.Topic, TopicPurgeCrashContract.Partition.TransactionDomainId) })
        {
            CrashDatabase.Submit(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(TopicPurgeCrashContract.Partition.TenantId,
            TopicPurgeCrashContract.Partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
    }
    private static void ConfigurePin(DatabaseEngine database, bool pinned)
    {
        var id = Guid.NewGuid();
        CrashDatabase.Submit(database, OperationKind.ConfigureSubscription,
            new ConfigureSubscriptionRequest(id, TopicPurgeCrashContract.Subscription, new(CrashFixtureValues.Principal),
                pinned ? SubscriptionStart.FromBeginning : SubscriptionStart.FromNow), id).Get<SubscriptionInfo>();
        id = Guid.NewGuid();
        CrashDatabase.Submit(database, OperationKind.SetSubscriptionPaused,
            new SetSubscriptionPausedRequest(id, TopicPurgeCrashContract.Subscription, SourceGeneration, true), id).Get<SubscriptionInfo>();
    }
    private static async Task SaveAsync(string directory, ReplicatedOperation publish, OperationResult publishResult,
        ReplicatedOperation seed, OperationResult seedResult, ReplicatedOperation operation, ZoneTreeStore store, bool pinned, string cursor)
    {
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.PublishOperationFile, publish);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.PublishReceiptFile, publishResult);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.SeedOperationFile, seed);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.SeedReceiptFile, seedResult);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.OperationFile, operation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.PositionFile, store.Position);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.PinnedFile, pinned);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.CursorFile, cursor);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(directory, TopicPurgeCrashContract.ProtectedFile, ProtectedBytes(store));
    }
    internal static string[] ProtectedBytes(IAtomicStore store) => store.Read(view =>
    {
        var prefixes = new[] { DocumentSpace, MessageMetadataSpace, MessageBodySpace, QueueCountersSpace, ReadySpace, SubscriptionSpace, SubscriptionWindowSpace, SubscriptionCompletionSpace, SubscriptionInboxSpace };
        return prefixes.SelectMany(prefix =>
        {
            var page = view.Scan(KeySpace.Partition(prefix, TopicPurgeCrashContract.Partition), TopicPurgeCrashContract.FixtureRecordLimit);
            if (page.HasMore)
            { throw new InvalidOperationException(TruncatedInventory); }
            return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + RecordSeparator + Convert.ToHexString(record.Value.Span));
        }).ToArray();
    });
}
