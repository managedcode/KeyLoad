using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class EventingArtifactFixture
{
    private const string Prefix = "keyload-eventing-artifact-";
    private const string RootEvidenceKey = "KeyLoad.EventingArtifact.RetainedRoot";
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

    internal static async Task RunAsync(Func<EventingArtifactFixture, Task> operation)
    {
        var fixture = new EventingArtifactFixture();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            Directory.CreateDirectory(fixture.Root);
            fixture.Source = new(directory: Path.Combine(fixture.Root, SourceDirectory));
        }, failures);
        if (failures.Count == 0)
        { await ServerFailureObserver.ObserveAsync(() => operation(fixture), failures); }
        if (fixture.Target is { } target)
        { ServerFailureObserver.Observe(target.Dispose, failures); }
        if (fixture.Source is { } source)
        { ServerFailureObserver.Observe(source.Store.Dispose, failures); }
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(fixture.Root, true), failures); }
        foreach (var failure in failures)
        { failure.Data[RootEvidenceKey] = fixture.Root; }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void InitializeTime() => Time = Source.Database.EvaluationClock.GetUtcNow();
    internal void OpenTarget(string path)
    {
        Target = new(new(path), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = BackupIndexedDocumentDedupFixture.CreateDatabase(Target);
    }
    internal void ReopenTarget(string path)
    { Target!.Dispose(); Target = null; OpenTarget(path); }
    internal ReplicatedOperation Operation<T>(OperationKind kind, T value, Guid id, string principal = Principal)
        => new(id, kind, principal, Time, JsonSerializer.Serialize(value, JsonDefaults.Options));
    internal OperationResult Apply<T>(DatabaseEngine database, OperationKind kind, T value, Guid id, string principal = Principal)
        => database.Apply(Operation(kind, value, id, principal));
}
