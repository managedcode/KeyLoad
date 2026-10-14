using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitiveSafeR52Seed
{
    internal static async Task<EventMessageSensitiveSafeR52State> RunAsync(RequestCqrsRf3Callers root,
        bool subscription, bool dataAuthority, bool header, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(EventMessageSensitiveSafeR52Protocol.TenantPrefix + suffix, EventMessageSensitiveSafeR52Protocol.Database, EventMessageSensitiveSafeR52Protocol.Domain, suffix);
        var policy = new SensitiveFieldPolicy(EventMessageSensitiveSafeR52Protocol.MissingPath,
            EventMessageSensitivePublicProtocol.Classification, EventMessageSensitivePublicProtocol.GrantBefore);
        var resource = new ResourceDefinition(EventMessageSensitiveSafeR52Protocol.Resource,
            subscription ? ResourceKind.Topic : ResourceKind.WorkQueue, partition.TransactionDomainId)
        { FieldPolicies = header ? [] : [policy], HeaderPolicies = header ? [policy] : [] };
        resource = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        var id = EventMessageSensitiveSafeR52Protocol.WorkerPrefix + suffix;
        var key = id + EventMessageSensitiveSafeR52Protocol.CredentialSuffix;
        var worker = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, resource.Name, EventMessageSensitiveSafeR52Protocol.WorkerCapabilities)],
            [EventMessageSensitivePublicProtocol.GrantBefore]);
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), worker, token));
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            KeyLoad.Core.DatabaseEngine.Credential(id, id, key), token));
        var placementRequest = new AtomicPartitionPlacementReadRequest(EventMessageSensitiveSafeR52Protocol.PlacementSchema, partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(placementRequest, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await root.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placementRequest, token))).Value);
        var state = new EventMessageSensitiveSafeR52State(new(partition, resource, worker, key, subscription, dataAuthority, header), placement);
        if (subscription)
        {
            await EventMessageSensitivePublicOperations.GroupAsync(root, state.Model, false, token);
            await EventMessageSensitivePublicOperations.GroupAsync(root, state.Model, true, token);
        }
        await PublishAsync(root, state, EventMessageSensitiveSafeR52Protocol.First, token);
        return state;
    }

    internal static async Task PublishAsync(RequestCqrsRf3Callers root, EventMessageSensitiveSafeR52State state,
        string id, CancellationToken token)
    {
        Mutation mutation = state.Model.Subscription
            ? new PublishTopic(state.Model.Resource.Name, [new(id, EventMessageSensitiveSafeR52Protocol.EventType,
                EventMessageSensitiveSafeR52Protocol.Payload, EventMessageSensitiveSafeR52Protocol.Headers)])
            : new EnqueueMessage(state.Model.Resource.Name, id, EventMessageSensitiveSafeR52Protocol.Payload,
                EventMessageSensitiveSafeR52Protocol.Headers);
        var command = new CommandRequest(Guid.NewGuid(), state.Model.Partition, [mutation]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Model.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan((long)EventMessageSensitiveSafeR52Protocol.EmptyCount);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var kind = state.Model.Subscription ? EventMessageSensitivePublicProtocol.PublishKind : EventMessageSensitivePublicProtocol.EnqueueKind;
        await SqlRf3Protocol.EqualAsync<MutationReceipt[]>([new(kind, state.Model.Resource.Name, id,
            state.Model.Subscription ? state.Published.Count + EventMessageSensitiveSafeR52Protocol.FirstPosition
                : EventMessageSensitiveSafeR52Protocol.InitialRevision)], receipt.Mutations.ToArray());
        state.Producers.Add((command, receipt));
        state.Published.Add(id);
        await EventMessageSensitiveSafeR52Current.CaptureAsync(root, state, token);
    }
}
