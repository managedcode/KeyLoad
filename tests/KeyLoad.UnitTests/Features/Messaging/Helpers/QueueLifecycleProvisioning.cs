using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleProvisioning
{
    internal static void Seed(TestDatabase fixture, bool byteSublimit)
    {
        var literal = new EnqueueMessage(QueueLifecycleTestProtocol.Queue, QueueLifecycleTestProtocol.Parked,
            QueueLifecycleTestProtocol.Payload, QueueLifecycleTestProtocol.Headers, OrderingKey: QueueLifecycleTestProtocol.OrderingKey);
        var body = new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson, literal.OrderingKey, JsonData.Fingerprint(literal));
        var bytes = NativeSerialization.Serialize(body).LongLength;
        if (QueueLifecycleAccountingAssertions.BodyBytes(QueueLifecycleTestProtocol.Pending) != bytes)
        { throw new InvalidOperationException(QueueLifecycleTestProtocol.BodyFixtureMismatch); }
        fixture.Configure(QueueLifecycleTestProtocol.Collection, ResourceKind.Collection);
        fixture.Configure(QueueLifecycleTestProtocol.Queue, ResourceKind.WorkQueue, fields: [new("/knowledge", "private-lifecycle")], queuePolicy: new()
        {
            MaxAttempts = QueueLifecycleTestProtocol.One,
            MaxStoredMessages = QueueLifecycleTestProtocol.Three,
            MaxDeadLetterMessages = byteSublimit ? null : QueueLifecycleTestProtocol.One,
            MaxDeadLetterBytes = byteSublimit ? bytes : null
        });
        var principal = new PrincipalRecord(QueueLifecycleTestProtocol.Administrator, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, QueueLifecycleTestProtocol.Queue, Capability.QueuePublish | Capability.QueueConsume
                | Capability.QueueAck | Capability.QueueInspect | Capability.DeadLettersRead | Capability.DeadLettersRedrive | Capability.QueueCancel),
             new(fixture.Partition.DatabaseId, QueueLifecycleTestProtocol.Collection, Capability.DocumentsWrite | Capability.DocumentsRead)], ["*"])
        { ClusterAdministrator = true };
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        { Id = QueueLifecycleTestProtocol.DeniedAdministrator, FieldGrants = [] })).Get<PrincipalRecord>();
    }
}
