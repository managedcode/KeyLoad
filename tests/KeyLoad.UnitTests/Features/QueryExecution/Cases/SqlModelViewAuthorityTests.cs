using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlModelViewAuthorityTests
{
    [Test]
    public async Task AcSqlView003RequiresPersistedQueryAndModelReadCapabilities()
    {
        using var database = SqlModelViewTestSupport.Create();
        SqlModelViewTestSupport.Seed(database);
        SqlModelViewTestSupport.Principal(database, "no-event-read", Capability.Query, Capability.None);
        SqlModelViewTestSupport.Principal(database, "no-queue-inspect", Capability.None, Capability.Query);
        SqlModelViewTestSupport.Principal(database, "no-query", Capability.EventsRead, Capability.QueueInspect);
        var engine = new QueryEngine(database.Database);

        var eventRead = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("no-event-read",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM EVENTS('events', 'stream-a')")));
        var queueInspect = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("no-queue-inspect",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM QUEUE_MESSAGES('jobs')")));
        var query = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("no-query",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM EVENTS('events', 'stream-a')")));

        await Assert.That(eventRead.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(queueInspect.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(query.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task AcSqlView004RedactsPayloadAndHeadersAndChecksUseGrantsBeforeScanning()
    {
        using var database = SqlModelViewTestSupport.Create();
        SqlModelViewTestSupport.Seed(database);
        SqlModelViewTestSupport.Principal(database, SqlModelViewTestSupport.Reader,
            Capability.Query | Capability.EventsRead, Capability.Query | Capability.QueueInspect);
        var engine = new QueryEngine(database.Database);

        var eventPage = engine.Execute(SqlModelViewTestSupport.Reader,
            SqlModelViewTestSupport.Request(database, "SELECT * FROM EVENTS('events', 'stream-a')"));
        var queuePage = engine.Execute(SqlModelViewTestSupport.Reader,
            SqlModelViewTestSupport.Request(database, "SELECT * FROM QUEUE_MESSAGES('jobs')"));
        var privateFilter = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlModelViewTestSupport.Reader,
            SqlModelViewTestSupport.Request(database,
                "SELECT * FROM EVENTS('events', 'stream-a') WHERE payload.secret = 'MODEL_VIEW_PRIVATE_CANARY'")));
        var privateOrder = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlModelViewTestSupport.Reader,
            SqlModelViewTestSupport.Request(database,
                "SELECT * FROM QUEUE_MESSAGES('jobs') ORDER BY headers.privateHeader")));

        await Assert.That(eventPage.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.Secret);
        await Assert.That(eventPage.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.HeaderSecret);
        await Assert.That(queuePage.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.Secret);
        await Assert.That(queuePage.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.HeaderSecret);
        await Assert.That(privateFilter.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(privateOrder.Code).IsEqualTo(ErrorCode.PermissionDenied);

        SqlModelViewTestSupport.Principal(database, "field-reader",
            Capability.Query | Capability.EventsRead, Capability.Query | Capability.QueueInspect,
            ["secret.read", "secret.use", "header.read", "header.use"]);
        var authorized = engine.Execute("field-reader", SqlModelViewTestSupport.Request(database,
            "SELECT payload.secret AS secret, headers.privateHeader AS privateHeader " +
            "FROM EVENTS('events', 'stream-a') WHERE payload.secret = 'MODEL_VIEW_PRIVATE_CANARY'"));
        await Assert.That(authorized.Rows.Single().Json).Contains(SqlModelViewTestSupport.Secret);
        await Assert.That(authorized.Rows.Single().Json).Contains(SqlModelViewTestSupport.HeaderSecret);
    }

    [Test]
    public async Task AcSqlView004DeadLetterBodyRequiresAnAdditionalPersistedGrant()
    {
        using var database = new TestDatabase();
        database.Configure(SqlModelViewTestSupport.Queue, ResourceKind.WorkQueue,
            queuePolicy: new() { MaxAttempts = 1 });
        database.Commit(new EnqueueMessage(SqlModelViewTestSupport.Queue, SqlModelViewTestSupport.MessageId,
            SqlModelViewTestSupport.Payload(SqlModelViewTestSupport.Secret, "queue-visible"),
            SqlModelViewTestSupport.Headers(SqlModelViewTestSupport.HeaderSecret)));
        var lane = new QueueLaneRef(database.Partition, SqlModelViewTestSupport.Queue);
        var receiveId = Guid.NewGuid();
        var delivery = database.Submit(OperationKind.Receive,
            new ReceiveRequest(receiveId, lane), id: receiveId).Get<ReceiveResult>().Deliveries.Single();
        var nackId = Guid.NewGuid();
        database.Submit(OperationKind.Delivery,
            new DeliveryCommand(nackId, lane, delivery.Token, DeliveryAction.Nack), id: nackId)
            .Get<CommitReceipt>();
        SqlModelViewTestSupport.Principal(database, "dead-letter-reader", Capability.None,
            Capability.Query | Capability.QueueInspect);

        var error = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(database.Database).Execute(
            "dead-letter-reader", SqlModelViewTestSupport.Request(database,
                "SELECT * FROM QUEUE_MESSAGES('jobs')")));

        await Assert.That(database.Database.InspectMessage("root", lane, SqlModelViewTestSupport.MessageId)!
            .Metadata.State).IsEqualTo(MessageState.DeadLettered);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
}
