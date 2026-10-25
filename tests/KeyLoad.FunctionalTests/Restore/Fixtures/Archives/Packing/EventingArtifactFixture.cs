using System.Text.Json;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class EventingArtifactFixture
{
    private const string OriginalEngineRequired = "The mixed command requires its original owned execution engine.";
    private const string Prefix = "keyload-eventing-artifact-";
    internal const string Principal = "root";
    internal const string Topic = "backup-topic";
    internal const string Queue = "backup-queue";
    internal const string Documents = "backup-documents";
    internal const string DocumentId = "processed";
    internal const string MessageId = "derived";
    internal const string FirstEvent = "one";
    internal const string SecondEvent = "two";
    internal const string ThirdEvent = "three";
    internal const string EventType = "Created";
    internal const string EmptyJson = "{}";
    private const string ProcessedGroup = "processed-group";
    private const string UnreadGroup = "unread-group";
    private const string SourceDirectory = "source";
    internal const string Handler = "backup-handler";
    internal const string Json = "{\"retained\":true}";
    internal string Root { get; } = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
        Prefix + Guid.NewGuid().ToString("N"));
    internal TestDatabase Source { get; private set; } = null!;
    internal ZoneTreeStore? Target
    {
        get; private set;
    }
    internal DatabaseEngine Database { get; private set; } = null!;
    internal EventSourceRef Events => new(Source.Partition, Topic, EventSourceKind.Topic);
    internal SubscriptionRef Group => new(Events, ProcessedGroup);
    internal SubscriptionRef Other => new(Events, UnreadGroup);
    internal QueueLaneRef Lane => new(Source.Partition, Queue);
    internal DateTimeOffset Time
    {
        get; private set;
    }
    internal ReceiveSubscriptionResult Received { get; set; } = null!;
    internal ReceiveResult QueueClaim { get; set; } = null!;
    internal SubscriptionProcessingResult Processed { get; set; } = null!;
    internal Command<DatabaseCommand> Original { get; set; } = null!;
    internal string Cursor { get; set; } = null!;
    internal static Mutation[] Effects => [new PutDocument(Documents, DocumentId, Json, 0), new EnqueueMessage(Queue, MessageId, Json)];

    internal static Task RunAsync(Func<EventingArtifactFixture, Task> operation)
        => EventingArtifactFixtureLifetime.RunAsync(new(), operation);

    internal void InitializeSource()
    {
        Directory.CreateDirectory(Root);
        Source = new(directory: Path.Combine(Root, SourceDirectory));
    }

    internal void InitializeTime() => Time = Source.Database.EvaluationClock.GetUtcNow();
    internal void OpenTarget(string path)
    {
        Target = new(new(path), Source.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        Database = Source.CreateConfiguredDatabase(Target);
    }
    internal (long AppliedPosition, long StorePosition) ReadTargetCut()
        => Database.Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : default(long), Database.Store.Position));

    internal Command<DatabaseCommand> ReadTargetIssuedOperation(Guid originalId)
        => OutcomeStoreOracle.ReadStored(Database, originalId, CancellationToken.None).PreparedCommand;

    internal void ReopenTarget(string path)
    {
        JoinTarget();
        Target = null;
        OpenTarget(path);
    }
    internal void JoinTarget()
    {
        Target?.Dispose();
    }
    internal Command<DatabaseCommand> Operation<T>(OperationKind kind, T value, Guid id, string principal = Principal)
    {
        var database = Target is null ? Source.Database : Database;
        var committed = QueueWholeFlowStorage.Clock(Target ?? Source.Store);
        return database.CreateCommand(kind, id, principal, Time < committed ? committed : Time,
            JsonSerializer.Serialize(value, JsonDefaults.Options));
    }
    internal Result<object> Apply<T>(DatabaseEngine database, OperationKind kind, T value, Guid id, string principal = Principal)
        => SubmitIssued(database, Operation(kind, value, id, principal), explicitTime: true);

    internal Result<object> ApplyInbox(DatabaseEngine database, CommitInboxRequest request, CancellationToken token)
        => SubmitIssued(database, database.CreateCommand(OperationKind.CommitInbox, request.CommandId, Principal, default,
            JsonSerializer.Serialize(request, JsonDefaults.Options)), explicitTime: false, token);

    internal Result<object> SubmitIssued(DatabaseEngine database, Command<DatabaseCommand> operation,
        bool explicitTime = true, CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(database, Source.Database) && !ReferenceEquals(database, Database))
        {
            throw new InvalidOperationException(OriginalEngineRequired);
        }
        return explicitTime ? database.Apply(operation) : database.ApplyEmbedded(operation, cancellationToken);
    }
}
