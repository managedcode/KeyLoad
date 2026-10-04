using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SourceReadBudgetTests
{
    private const string TopicName = "source-read-topic";
    private const string OtherTopicName = "source-read-other-topic";
    private const string ReaderId = "source-reader";
    private const string EventType = "Created";
    private const string CursorPurpose = "event-source-page";
    private const string TopicHeadSpace = "topic-head";
    private const string TopicEventSpace = "topic-event";
    private const string FailureDirectoryPrefix = "keyload-source-read-failure-";
    private const string GuidFormat = "N";
    private const int TooSmallBatchByteLimit = 1;

    [Test]
    public async Task AcMp005ExactRawReadBudgetSucceedsAndOneByteShortBudgetFails()
    {
        using var database = CreateTopicDatabase();
        var requiredBytes = TopicReadBytes(database);
        var exact = new DatabaseEngine(database.Store, database.Database.Authorization,
            new() { MaxQueryReadBytes = requiredBytes });
        var shortBudget = new DatabaseEngine(database.Store, database.Database.Authorization,
            new() { MaxQueryReadBytes = requiredBytes - 1 });
        var source = new EventSourceRef(database.Partition, TopicName, EventSourceKind.Topic);

        var before = database.Store.GetReadDiagnostics();
        var exactPage = exact.ReadEventSource(ReaderId, new(source));
        var after = database.Store.GetReadDiagnostics();
        await Assert.That(exactPage.Events).HasSingleItem();
        await Assert.That(after.PointExaminedBytes - before.PointExaminedBytes).IsEqualTo(requiredBytes);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => shortBudget.ReadEventSource(ReaderId, new(source)));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(exact.ReadEventSource(ReaderId, new(source)).Events).HasSingleItem();
    }

    [Test]
    public async Task AcMp005PreCancelledReadAndTamperedCursorDoNotPoisonLaterReads()
    {
        using var database = CreateTopicDatabase();
        var source = new EventSourceRef(database.Partition, TopicName, EventSourceKind.Topic);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => database.Database.ReadEventSource(ReaderId, new(source), cancelled.Token));

        var page = database.Database.ReadEventSource(ReaderId, new(source));
        var altered = (page.Cursor[0] == 'A' ? "B" : "A") + page.Cursor[1..];
        var tampered = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource(ReaderId, new(source, Cursor: altered)));
        await Assert.That(tampered.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(database.Database.ReadEventSource(ReaderId, new(source)).Events).HasSingleItem();
    }

    [Test]
    public async Task AcMp005CursorScopeExpiryAndEmptyEnvelopeLimitsAreValidated()
    {
        using var database = CreateTopicDatabase();
        ConfigureTopic(database, OtherTopicName);
        var source = new EventSourceRef(database.Partition, TopicName, EventSourceKind.Topic);
        var page = database.Database.ReadEventSource("root", new(source));
        var otherSource = new EventSourceRef(database.Partition, OtherTopicName, EventSourceKind.Topic);
        var wrongScope = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource("root",
            new(otherSource, Cursor: page.Cursor)));
        await Assert.That(wrongScope.Code).IsEqualTo(ErrorCode.CursorExpired);
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource("root",
            new(source with { Generation = 2 })));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.TokenInvalidated);

        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, TopicName)))!;
        var expired = database.Database.Sign(new SourceCursor(CursorPurpose, database.Store.Identity.Incarnation,
            source, principal.Id, principal.PolicyEpoch, resource.SchemaVersion, 0L, DateTimeOffset.UnixEpoch));
        var expiredError = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource("root",
            new(source, Cursor: expired)));
        await Assert.That(expiredError.Code).IsEqualTo(ErrorCode.CursorExpired);

        using var empty = new TestDatabase();
        ConfigureTopic(empty, TopicName);
        var limited = new DatabaseEngine(empty.Store, empty.Database.Authorization, new() { MaxBatchBytes = TooSmallBatchByteLimit });
        var envelopeError = Assert.ThrowsExactly<KeyLoadException>(() => limited.ReadEventSource("root",
            new(new(empty.Partition, TopicName, EventSourceKind.Topic))));
        await Assert.That(envelopeError.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcMp005TopicFactoryFailureReleasesOwnedDirectoryAndAllowsSamePathReopen()
    {
        var directory = NewFailureDirectory();
        try
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            {
                using var unexpected = CreateTopicDatabase(
                    new() { MaxBatchBytes = TooSmallBatchByteLimit }, directory);
            });
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(Directory.Exists(directory)).IsFalse();

            using var reopened = new TestDatabase(directory: directory);
            await Assert.That(Directory.Exists(directory)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static TestDatabase CreateTopicDatabase(DatabaseLimits? limits = null, string? directory = null)
    {
        var database = new TestDatabase(limits, directory);
        try
        {
            ConfigureTopic(database, TopicName);
            var reader = new PrincipalRecord(ReaderId, database.Partition.TenantId,
                [new(database.Partition.DatabaseId, TopicName, Capability.TopicsRead)], []);
            database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader),
                time: SourceReadBusinessTime.Next(database)).Get<PrincipalRecord>();
            PublishTopic(database, [new("topic-a", EventType, "{}")]);
            return database;
        }
        catch (Exception)
        {
            database.Dispose();
            throw;
        }
    }

    private static string NewFailureDirectory()
    {
        string directory;
        do
        {
            directory = Path.Combine(Path.GetTempPath(), FailureDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        }
        while (Directory.Exists(directory) || File.Exists(directory));

        return directory;
    }

    private static long TopicReadBytes(TestDatabase database)
    {
        var keys = new[]
        {
            KeySpace.Principal(ReaderId),
            KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, TopicName),
            KeySpace.Partition(TopicHeadSpace, database.Partition, TopicName),
            KeySpace.Partition(TopicEventSpace, database.Partition, TopicName, 1L, 1L)
        };
        return database.Store.Read(view =>
        {
            var bytes = 0L;
            foreach (var key in keys)
            {
                view.ReadValue(key, static _ => { }, count => bytes += count);
            }
            return bytes;
        });
    }

    private static void ConfigureTopic(TestDatabase database, string name)
    {
        var resource = new ResourceDefinition(name, ResourceKind.Topic, database.Partition.TransactionDomainId);
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource),
            time: SourceReadBusinessTime.Next(database)).Get<ResourceDefinition>();
    }

    private static void PublishTopic(TestDatabase database, EventData[] events)
    {
        var id = Guid.NewGuid();
        database.Submit(OperationKind.Batch, new CommandRequest(id, database.Partition,
            [new PublishTopic(TopicName, [.. events])]), id: id,
            time: SourceReadBusinessTime.Next(database)).Get<CommitReceipt>();
    }
}
