using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class EventingArtifactFixture
{
    private const string OriginalEngineRequired = "The mixed command requires its original owned native engine.";
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
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
    internal TestDatabase Source { get; private set; } = null!;
    internal ZoneTreeStore? Target { get; private set; }
    internal DatabaseEngine Database { get; private set; } = null!;
    private TestDatabaseReplicaAdmission? targetAdmission;
    private bool nativeAdmission;
    private Guid? originalTargetJournalNodeId;
    internal EventSourceRef Events => new(Source.Partition, Topic, EventSourceKind.Topic);
    internal SubscriptionRef Group => new(Events, ProcessedGroup);
    internal SubscriptionRef Other => new(Events, UnreadGroup);
    internal QueueLaneRef Lane => new(Source.Partition, Queue);
    internal DateTimeOffset Time { get; private set; }
    internal ReceiveSubscriptionResult Received { get; set; } = null!;
    internal ReceiveResult QueueClaim { get; set; } = null!;
    internal SubscriptionProcessingResult Processed { get; set; } = null!;
    internal ReplicatedOperation Original { get; set; } = null!;
    internal string Cursor { get; set; } = null!;
    internal static Mutation[] Effects => [new PutDocument(Documents, DocumentId, Json, 0), new EnqueueMessage(Queue, MessageId, Json)];

    internal static Task RunAsync(Func<EventingArtifactFixture, Task> operation, bool nativeAdmission = false)
        => EventingArtifactFixtureLifetime.RunAsync(new() { nativeAdmission = nativeAdmission }, operation);

    internal void InitializeSource()
    {
        Directory.CreateDirectory(Root);
        Source = new(directory: Path.Combine(Root, SourceDirectory), nativeReplicaAdmission: nativeAdmission,
            nativeReplicaDirectory: nativeAdmission ? Path.Combine(Root, SourceDirectory + TestDatabaseReplicaDirectory.Suffix) : null);
    }

    internal void InitializeTime() => Time = Source.Database.EvaluationClock.GetUtcNow();
    internal void OpenTarget(string path)
    {
        Target = new(new(path), nativeAdmission ? Source.StorageExecution : UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = nativeAdmission ? Source.CreateConfiguredDatabase(Target)
            : BackupIndexedDocumentDedupFixture.CreateDatabase(Target);
        if (nativeAdmission)
        {
            targetAdmission = Source.CreateTargetAdmission(Database, Target, path,
            Path.Combine(Root, Path.GetFileName(path) + TestDatabaseReplicaDirectory.Suffix), originalTargetJournalNodeId);
            originalTargetJournalNodeId ??= targetAdmission.JournalNodeId;
        }
    }
    internal (long LastIndex, long CommittedIndex, long Term) ReadTargetJournalCut()
        => targetAdmission?.ReadJournalCut() ?? throw new InvalidOperationException(OriginalEngineRequired);

    internal ReplicatedOperation ReadTargetIssuedOperation(Guid originalId)
        => targetAdmission?.ReadRetainedEntry(originalId).Operation
            ?? throw new InvalidOperationException(OriginalEngineRequired);

    internal void ReopenTarget(string path)
    { JoinTarget(); Target = null; OpenTarget(path); }
    internal void JoinTarget()
    {
        var original = targetAdmission;
        targetAdmission = null;
        if (Target is { } target)
        { TestDatabaseJoinedLifetime.DisposeInOrder(original, target); }
        else
        { original?.Dispose(); }
    }
    internal ReplicatedOperation Operation<T>(OperationKind kind, T value, Guid id, string principal = Principal)
        => new(id, kind, principal, nativeAdmission && Target is not null
            ? Database.EvaluationClock.GetUtcNow() : Time, JsonSerializer.Serialize(value, JsonDefaults.Options));
    internal OperationResult Apply<T>(DatabaseEngine database, OperationKind kind, T value, Guid id, string principal = Principal)
        => SubmitIssued(database, Operation(kind, value, id, principal), explicitTime: !nativeAdmission || ReferenceEquals(database, Source.Database));

    internal OperationResult ApplyInbox(DatabaseEngine database, CommitInboxRequest request, CancellationToken token)
        => SubmitIssued(database, new(request.CommandId, OperationKind.CommitInbox, Principal, default,
            JsonSerializer.Serialize(request, JsonDefaults.Options)), explicitTime: false, token);

    internal OperationResult SubmitIssued(DatabaseEngine database, ReplicatedOperation operation,
        bool explicitTime = true, CancellationToken cancellationToken = default)
        => !nativeAdmission ? explicitTime ? database.Apply(operation) : database.ApplyEmbedded(operation, cancellationToken)
            : ReferenceEquals(database, Source.Database)
                ? explicitTime ? Source.SubmitIssued(operation) : Source.SubmitIssuedEmbedded(operation, cancellationToken)
                : ReferenceEquals(database, Database) && targetAdmission is { } target
                    ? target.Submit(operation, explicitTime, cancellationToken)
                    : throw new InvalidOperationException(OriginalEngineRequired);
}
