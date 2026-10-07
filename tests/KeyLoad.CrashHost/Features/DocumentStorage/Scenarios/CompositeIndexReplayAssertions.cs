using KeyLoad.Core;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CompositeIndexReplayAssertions
{
    private const long OriginalExpectedRevision = 1;
    private const long ExpectedAbsentRevision = 0;
    private const int NoStreamEvents = 0;
    private const string EmptyJsonObject = "{}";
    private const string Stream = "composite-events";
    private const string Queue = "composite-jobs";
    private const string EventId = "rejected-event";
    private const string MessageId = "rejected-message";
    private const string OriginalFile = "composite-original-receipt.bin";
    private const string FailureFile = "composite-original-failure.bin";
    private const string Invalid = "The mixed composite unique conflict or original replay changed canonical effects.";
    private const int MaximumRecords = 8192;

    internal static void Prepare(string directory, DatabaseEngine database)
    {
        foreach (var resource in new[] { new ResourceDefinition(Stream, ResourceKind.StreamSet,
            CompositeIndexCrashContract.Partition.TransactionDomainId), new ResourceDefinition(Queue, ResourceKind.WorkQueue,
            CompositeIndexCrashContract.Partition.TransactionDomainId) })
        {
            _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(CompositeIndexCrashContract.Partition.TenantId,
                    CompositeIndexCrashContract.Partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
        var original = ReplaySuccess(database);
        File.WriteAllBytes(Path.Combine(directory, OriginalFile), NativeSerialization.Serialize(original.Get<CommitReceipt>()));
        var before = CompositeIndexCrashOperations.Capture(database);
        var failure = ApplyConflict(database);
        if (failure.Error != ErrorCode.Conflict)
        { throw new InvalidOperationException(Invalid); }
        File.WriteAllBytes(Path.Combine(directory, FailureFile), NativeSerialization.Serialize(failure));
        RequireEffectsUnchanged(database, before);
        Verify(directory, database);
    }

    internal static void Verify(string directory, DatabaseEngine database)
    {
        var before = CaptureAll(database);
        var position = database.Store.Position;
        var original = NativeSerialization.Serialize(ReplaySuccess(database).Get<CommitReceipt>());
        if (!original.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(directory, OriginalFile))))
        { throw new InvalidOperationException(Invalid); }
        var failed = NativeSerialization.Serialize(ApplyConflict(database));
        if (!failed.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(directory, FailureFile))) ||
            database.Store.Position != position || !before.AsSpan().SequenceEqual(CaptureAll(database)))
        { throw new InvalidOperationException(Invalid); }
        RequireEffectsUnchanged(database, CompositeIndexCrashOperations.Capture(database));
    }

    private static OperationResult ReplaySuccess(DatabaseEngine database)
    {
        var id = Guid.Parse(CompositeIndexCrashContract.ReplaceCommandText);
        return CrashDatabase.Submit(database, OperationKind.Batch, new CommandRequest(id, CompositeIndexCrashContract.Partition,
            [new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.FirstId,
                CompositeIndexCrashContract.ReplacedJson, OriginalExpectedRevision, ExplicitReplacement: true)]), id);
    }

    private static OperationResult ApplyConflict(DatabaseEngine database)
    {
        var id = Guid.Parse(CompositeIndexCrashContract.ConflictCommandText);
        return CrashDatabase.Submit(database, OperationKind.Batch, new CommandRequest(id, CompositeIndexCrashContract.Partition,
            [new AppendEvents(Stream, EventId, [new(EventId, EventId, EmptyJsonObject)], ExpectedStreamRevision.NoStream),
                new EnqueueMessage(Queue, MessageId, EmptyJsonObject),
                new PutDocument(CompositeIndexCrashContract.Collection, CompositeIndexCrashContract.ConflictId,
                    CompositeIndexCrashContract.ReplacedJson, ExpectedAbsentRevision)]), id);
    }

    private static void RequireEffectsUnchanged(DatabaseEngine database, CompositeIndexCrashSnapshot expected)
    {
        if (!JsonDefaults.Serialize(CompositeIndexCrashOperations.Capture(database)).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)) ||
            database.ReadStream(CrashFixtureValues.Principal, new(CompositeIndexCrashContract.Partition, Stream, EventId)).Events.Length != NoStreamEvents ||
            database.InspectMessage(CrashFixtureValues.Principal, new(CompositeIndexCrashContract.Partition, Queue), MessageId) is not null)
        { throw new InvalidOperationException(Invalid); }
    }

    private static byte[] CaptureAll(DatabaseEngine database) => database.Store.Read(view =>
    {
        var page = view.Scan([], MaximumRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(Invalid); }
        return JsonDefaults.Serialize(page.Records);
    });
}
