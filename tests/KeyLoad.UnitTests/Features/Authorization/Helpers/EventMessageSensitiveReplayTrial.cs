using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventMessageSensitiveReplayTrial
{
    internal static async Task RunAsync(bool subscription, bool dataAuthority, bool header, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var state = await SeedAsync(fixture, subscription, dataAuthority, header, token);
        state.Resource = EventMessageSensitiveReplayOperations.ReplacePolicy(fixture.Database, state, false, token);
        await EventMessageSensitiveReplayAssertions.RefusalAsync(fixture.Database, fixture.Store, state, token);
        await EventMessageSensitiveReplayAssertions.RedactedAsync(fixture.Database, state, token);
        state.Resource = EventMessageSensitiveReplayOperations.ReplacePolicy(fixture.Database, state, true, token);
        var freshId = Guid.NewGuid();
        var denied = EventMessageSensitiveReplayOperations.Apply(fixture.Database, state.ReceiveKind,
            EventMessageSensitiveReplayOperations.Receive(state, freshId, true), state.Caller, freshId, token);
        await EventMessageSensitiveReplayAssertions.DeniedAsync(denied, ErrorCode.PermissionDenied);
        await EventMessageSensitiveReplayContinuation.RepairAsync(fixture.Database, state, token);
        await EventMessageSensitiveReplayAssertions.RefusalAsync(fixture.Database, fixture.Store, state, token);
        await EventMessageSensitiveReplayContinuation.HealthyAsync(fixture.Database, state, token);
        var identity = fixture.Store.Identity;
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await EventMessageSensitiveReplayAssertions.RefusalAsync(database, reopened, state, token);
        await EventMessageSensitiveReplayAssertions.HealthyAsync(database, state, token);
        await EventMessageSensitiveReplayContinuation.CompleteAsync(database, state, token);
    }

    private static async Task<EventMessageSensitiveReplayState> SeedAsync(TestDatabase fixture,
        bool subscription, bool dataAuthority, bool header, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var policy = new SensitiveFieldPolicy(EventMessageSensitiveReplayProtocol.SecretPath,
            EventMessageSensitiveReplayProtocol.Classification, EventMessageSensitiveReplayProtocol.GrantBefore);
        var resource = new ResourceDefinition(EventMessageSensitiveReplayProtocol.Resource,
            subscription ? ResourceKind.Topic : ResourceKind.WorkQueue, fixture.Partition.TransactionDomainId)
        { FieldPolicies = header ? [] : [policy], HeaderPolicies = header ? [policy] : [] };
        resource = fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        var capabilities = Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect
            | Capability.TopicsRead | Capability.SubscriptionsConsume | Capability.SubscriptionsAck;
        var worker = new PrincipalRecord(EventMessageSensitiveReplayProtocol.Worker, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, resource.Name, capabilities)], [EventMessageSensitiveReplayProtocol.GrantBefore]);
        EventMessageSensitiveReplayOperations.Principal(fixture.Database, worker, token);
        var state = new EventMessageSensitiveReplayState(fixture.Partition, resource, worker, subscription, dataAuthority, header)
        {
            Producer = new(Guid.NewGuid(), fixture.Partition, subscription
                ? [new PublishTopic(resource.Name, [new(EventMessageSensitiveReplayProtocol.First, EventMessageSensitiveReplayProtocol.EventType,
                    EventMessageSensitiveReplayProtocol.Payload, EventMessageSensitiveReplayProtocol.Headers)])]
                : [new EnqueueMessage(resource.Name, EventMessageSensitiveReplayProtocol.First, EventMessageSensitiveReplayProtocol.Payload, EventMessageSensitiveReplayProtocol.Headers),
                   new EnqueueMessage(resource.Name, EventMessageSensitiveReplayProtocol.Second, EventMessageSensitiveReplayProtocol.Payload, EventMessageSensitiveReplayProtocol.Headers)])
        };
        state.Produced = EventMessageSensitiveReplayOperations.Apply(fixture.Database, OperationKind.Batch,
            state.Producer, EventMessageSensitiveReplayProtocol.Root, state.Producer.CommandId, token);
        state.Produced.Get<CommitReceipt>();
        if (subscription)
        {
            EventMessageSensitiveReplayOperations.ConfigureGroup(fixture.Database, state, false, token);
            EventMessageSensitiveReplayOperations.ConfigureGroup(fixture.Database, state, true, token);
        }
        state.OriginalId = Guid.NewGuid();
        state.OriginalRequest = EventMessageSensitiveReplayOperations.Receive(state, state.OriginalId, false);
        state.Original = EventMessageSensitiveReplayOperations.Apply(fixture.Database, state.ReceiveKind,
            state.OriginalRequest, state.Caller, state.OriginalId, token);
        await EventMessageSensitiveReplayAssertions.FullAsync(state.Original, state);
        state.OriginalOutcome = EventMessageSensitiveReplayOperations.Outcome(fixture.Store, state);
        return state;
    }
}
