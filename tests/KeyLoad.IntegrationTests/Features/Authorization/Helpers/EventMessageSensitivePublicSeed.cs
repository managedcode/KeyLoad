using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicSeed
{
    internal static async Task<EventMessageSensitivePublicState> RunAsync(RequestCqrsRf3Callers root,
        bool subscription, bool dataAuthority, bool header, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef("sensitive-" + suffix, "database", "messaging", suffix);
        var policy = new SensitiveFieldPolicy(EventMessageSensitivePublicProtocol.Path,
            EventMessageSensitivePublicProtocol.Classification, EventMessageSensitivePublicProtocol.GrantBefore);
        var resource = new ResourceDefinition(EventMessageSensitivePublicProtocol.Queue,
            subscription ? ResourceKind.Topic : ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            FieldPolicies = header ? [] : [policy],
            HeaderPolicies = header ? [policy] : [],
            QueuePolicy = subscription ? new() : new() { MaxAttempts = EventMessageSensitivePublicProtocol.SingleAttempt }
        };
        resource = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        var id = "sensitive-worker-" + suffix;
        var key = id + ".keyload-native-sensitive-credential";
        var worker = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, resource.Name, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect
                | Capability.DeadLettersRead | Capability.TopicsRead | Capability.SubscriptionsConsume | Capability.SubscriptionsAck)],
            [EventMessageSensitivePublicProtocol.GrantBefore]);
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), worker, token));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            KeyLoad.Core.DatabaseEngine.Credential(id, id, key), token))).IsTrue();
        var state = new EventMessageSensitivePublicState(partition, resource, worker, key, subscription, dataAuthority, header);
        var inspector = worker with { Id = state.InspectorId, FieldGrants = [] };
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), inspector, token));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            KeyLoad.Core.DatabaseEngine.Credential(inspector.Id, inspector.Id, state.InspectorKey), token))).IsTrue();
        var command = new CommandRequest(Guid.NewGuid(), partition, subscription
            ? [new PublishTopic(resource.Name, [new(EventMessageSensitivePublicProtocol.Message, EventMessageSensitivePublicProtocol.EventType,
                EventMessageSensitivePublicProtocol.Payload, EventMessageSensitivePublicProtocol.Headers)])]
            : [new EnqueueMessage(resource.Name, EventMessageSensitivePublicProtocol.Message,
                EventMessageSensitivePublicProtocol.Payload, EventMessageSensitivePublicProtocol.Headers)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(default(long));
        await Assert.That(receipt.Token.OwnershipEpoch).IsGreaterThan(default(long));
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(mutation).IsEqualTo(new MutationReceipt(subscription ? EventMessageSensitivePublicProtocol.PublishKind
            : EventMessageSensitivePublicProtocol.EnqueueKind, resource.Name, EventMessageSensitivePublicProtocol.Message,
            EventMessageSensitivePublicProtocol.InitialRevision));
        state.Commands.Add((command, receipt));
        if (subscription)
        {
            await EventMessageSensitivePublicOperations.GroupAsync(root, state, false, token);
            await EventMessageSensitivePublicOperations.GroupAsync(root, state, true, token);
        }
        return state;
    }
}
