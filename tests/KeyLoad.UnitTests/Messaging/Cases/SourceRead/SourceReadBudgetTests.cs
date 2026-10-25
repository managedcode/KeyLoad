using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Messaging;

internal sealed class SourceReadBudgetTests
{
    private const string TopicName = "source-read-topic";
    private const string OtherTopicName = "source-read-other-topic";
    private const string ReaderId = "source-reader";
    private const string EventType = "Created";
    private const string CursorPurpose = "event-source-page";

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
    public async Task AcMp005CursorScopeAndExpiryAreValidated()
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
            new(source with
            {
                Generation = 2
            })));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.TokenInvalidated);

        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, TopicName)))!;
        var expired = database.Database.Sign(new SourceCursor(CursorPurpose, database.Store.Identity.Incarnation,
            source, principal.Id, principal.PolicyEpoch, resource.SchemaVersion, 0L, DateTimeOffset.UnixEpoch));
        var expiredError = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource("root",
            new(source, Cursor: expired)));
        await Assert.That(expiredError.Code).IsEqualTo(ErrorCode.CursorExpired);
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
