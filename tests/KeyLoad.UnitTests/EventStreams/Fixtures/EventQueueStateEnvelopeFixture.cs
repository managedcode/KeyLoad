using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class EventQueueStateEnvelopeFixture : IDisposable
{
    private const int NoFailures = 0;
    private const string MissingOriginalRoot = "The original envelope owner root must remain canonical, existing and nonempty.";
    internal TestDatabase Owner { get; } = new();
    internal ZoneTreeStore Store
    {
        get; private set;
    }
    internal DatabaseEngine Database
    {
        get; private set;
    }
    internal PartitionRef Partition => Owner.Partition;
    internal QueueLaneRef Lane => new(Partition, Queue);
    internal StreamRef Stream => new(Partition, Streams, Item, InitialGeneration);
    internal EventSourceRef TopicSource => new(Partition, Topic, EventSourceKind.Topic);
    internal SubscriptionRef Subscription => new(TopicSource, Group);
    internal SubscriptionDefinition Definition { get; } = new(Root);
    internal DateTimeOffset At
    {
        get; private set;
    }
    internal DateTimeOffset ScheduledAt => At.AddSeconds(ScheduledDelaySeconds);

    internal EventQueueStateEnvelopeFixture()
    {
        Store = Owner.Store;
        Database = Owner.Database;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Initialize, failures);
        if (failures.Count != NoFailures)
        {
            ServerFailureObserver.Observe(Owner.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private void Initialize()
    {
        Configure(Documents, ResourceKind.Collection, classified: false);
        Configure(Graph, ResourceKind.Graph, classified: false);
        Configure(Streams, ResourceKind.StreamSet, classified: true);
        Configure(Queue, ResourceKind.WorkQueue, classified: true);
        Configure(Topic, ResourceKind.Topic, classified: true);
        At = QueueWholeFlowStorage.Clock(Store).AddTicks(AfterConfigurationTick);
    }

    internal void Reopen()
    {
        RequireOriginalRoot();
        var identity = KeyLoad.UnitTests.Features.StorageRecovery.Assertions.ReadCutStoreStateAssertions.SnapshotIdentity(Store);
        Store.Dispose();
        Store = new(new(Owner.Directory)
        {
            Incarnation = identity.Incarnation,
            SigningKey = identity.SigningKey
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = Owner.CreateConfiguredDatabase(Store);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Store.Dispose, failures);
        ServerFailureObserver.Observe(Owner.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void RequireOriginalRoot()
    {
        if (!Path.IsPathFullyQualified(Owner.Directory)
            || !string.Equals(Path.GetFullPath(Owner.Directory), Owner.Directory, StringComparison.Ordinal)
            || !System.IO.Directory.Exists(Owner.Directory)
            || !System.IO.Directory.EnumerateFileSystemEntries(Owner.Directory).Any())
        {
            throw new InvalidOperationException(MissingOriginalRoot);
        }
    }

    private void Configure(string name, ResourceKind kind, bool classified)
    {
        var definition = new ResourceDefinition(name, kind, Partition.TransactionDomainId)
        {
            FieldPolicies = classified ? [new(SecretPath, Classification)] : [],
            HeaderPolicies = classified ? [new(SecretPath, Classification)] : []
        };
        _ = Owner.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, definition)).Get<ResourceDefinition>();
    }
}
