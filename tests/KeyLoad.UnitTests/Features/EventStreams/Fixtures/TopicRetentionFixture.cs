using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class TopicRetentionFixture : IDisposable
{
    internal const string Topic = "topic";
    internal const string Queue = "work";
    internal const string Documents = "orders";
    internal const string Payload = "{\"n\":1}";
    internal TestDatabase Owner { get; }
    internal ZoneTreeStore Store { get; private set; }
    internal DatabaseEngine Database { get; private set; }
    internal EventSourceRef Source { get; }
    internal CommandRequest Publication { get; }
    internal CommitReceipt PublicationReceipt { get; }
    internal DateTimeOffset PublicationAt { get; }
    internal SubscriptionRef First { get; }
    internal SubscriptionRef Second { get; }
    internal ReceiveSubscriptionResult Claims { get; }
    internal string OriginalCursor { get; }

    internal TopicRetentionFixture(DatabaseLimits? limits = null)
    {
        Owner = new(limits);
        Store = Owner.Store;
        Database = Owner.Database;
        try
        {
            Source = new(Owner.Partition, Topic, EventSourceKind.Topic);
            Owner.Configure(Topic, ResourceKind.Topic);
            Owner.Configure(Queue, ResourceKind.WorkQueue);
            Owner.Configure(Documents, ResourceKind.Collection);
            Publication = CreatePublication(Owner.Partition);
            PublicationAt = SourceReadBusinessTime.Next(Owner);
            PublicationReceipt = Owner.Submit(OperationKind.Batch, Publication, id: Publication.CommandId,
                time: PublicationAt).Get<CommitReceipt>();
            First = SubscriptionTestActions.Configure(Owner, "first");
            Second = SubscriptionTestActions.Configure(Owner, "second");
            Claims = SubscriptionTestActions.Receive(Owner, First);
            ProcessFirst();
            SubscriptionTestActions.Complete(Owner, First, Claims.Deliveries[2]);
            var pause = Guid.NewGuid();
            Owner.Submit(OperationKind.SetSubscriptionPaused, new SetSubscriptionPausedRequest(pause, Second, 1, true),
                id: pause).Get<SubscriptionInfo>();
            OriginalCursor = Database.ReadEventSource("root", new(Source, Limit: 1)).Cursor;
        }
        catch (Exception primary)
        {
            try
            { Owner.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private static CommandRequest CreatePublication(PartitionRef partition)
    {
        return new(Guid.NewGuid(), partition, [new PublishTopic(Topic,
            [new("event1", "Created", Payload), new("event2", "Created", "{\"n\":2}"),
             new("event3", "Created", "{\"n\":3}")])]);
    }
    private void ProcessFirst()
    {
        var process = Guid.NewGuid();
        Owner.Submit(OperationKind.SubscriptionProcessing, new SubscriptionProcessingRequest(process, First,
            Claims.Deliveries[0].Token, "handler", 1,
            [new PutDocument(Documents, "derived", "{\"done\":true}"), new EnqueueMessage(Queue, "derived", "{\"input\":1}")]),
            id: process).Get<SubscriptionProcessingResult>();
    }

    internal CommandRequest Purge(long cut = 2, long generation = 1)
        => new(Guid.NewGuid(), Owner.Partition, [new PurgeTopic(Topic, cut, generation)]);
    internal OperationResult Submit(CommandRequest command, string principal = "root")
        => Database.ApplyEmbedded(new(command.CommandId, OperationKind.Batch, principal, default,
            JsonSerializer.Serialize(command, JsonDefaults.Options)), cancellationToken: default);
    internal void ReleasePins()
    {
        SubscriptionTestActions.Complete(Owner, First, Claims.Deliveries[1]);
        var seek = Guid.NewGuid();
        Owner.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seek, Second, 1, SubscriptionStart.FromNow), id: seek).Get<SubscriptionInfo>();
    }
    internal void SetLimits(DatabaseLimits limits)
    {
        Database = new(Store, new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
    }
    internal void Reopen()
    {
        Store.Dispose();
        Store = new(new(Owner.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = QueueWholeFlowStorage.Open(Store);
    }
    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Store.Dispose, failures);
        ServerFailureObserver.Observe(Owner.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
