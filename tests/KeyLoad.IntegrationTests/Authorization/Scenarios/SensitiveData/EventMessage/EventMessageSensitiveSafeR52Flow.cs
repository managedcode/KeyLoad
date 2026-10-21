using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitiveSafeR52Flow
{
    internal static async Task WarmAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        EventMessageSensitiveSafeR52State state, CancellationToken token)
        => await ReadersAsync(fixture, state, async worker =>
        {
            var caller = state.Model.DataAuthority ? root : worker;
            state.OriginalRequest = EventMessageSensitivePublicOperations.Receive(state.Model, Guid.NewGuid(), false);
            state.Original = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state.Model,
                state.OriginalRequest, QueueLifecyclePublicProtocol.Routes.First(), token);
            await EventMessageSensitiveSafeR52Replay.LiteralAsync(state.Original, state, EventMessageSensitiveSafeR52Protocol.First);
            state.Leased.Add(EventMessageSensitiveSafeR52Protocol.First);
            await EventMessageSensitiveSafeR52Current.CaptureAsync(root, state, token);
            var policy = new SensitiveFieldPolicy(EventMessageSensitiveSafeR52Protocol.MissingPath,
                EventMessageSensitivePublicProtocol.Classification, EventMessageSensitivePublicProtocol.GrantAfter);
            var definition = state.Model.Header ? state.Model.Resource with
            {
                SchemaVersion = state.Model.Resource.SchemaVersion + 1L,
                HeaderPolicies = [policy]
            }
                : state.Model.Resource with
                {
                    SchemaVersion = state.Model.Resource.SchemaVersion + 1L,
                    FieldPolicies = [policy]
                };
            state.Model.Resource = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(state.Model.Partition.TenantId, state.Model.Partition.DatabaseId, definition)
                {
                    ExpectedSchemaVersion = state.Model.Resource.SchemaVersion
                }, token));
            await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
            await EventMessageSensitiveSafeR52Replay.ReceiveAsync(root, caller, state, state.OriginalRequest,
                state.Original, EventMessageSensitiveSafeR52Protocol.First, token);
            await EventMessageSensitiveSafeR52Replay.ReceiptsAsync(root, caller, state, token);
        }, token);

    internal static async Task AfterFirstColdAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        EventMessageSensitiveSafeR52State state, CancellationToken token)
        => await ReadersAsync(fixture, state, async worker =>
        {
            var caller = state.Model.DataAuthority ? root : worker;
            await EventMessageSensitiveSafeR52Replay.ReceiveAsync(root, caller, state, state.OriginalRequest,
                state.Original, EventMessageSensitiveSafeR52Protocol.First, token);
            await EventMessageSensitiveSafeR52Replay.ReceiptsAsync(root, caller, state, token);
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(),
                state.Model.Worker with
                {
                    Revoked = true,
                    PolicyEpoch = EventMessageSensitiveSafeR52Protocol.RevokedEpoch
                }, token));
            await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
            await EventMessageSensitiveSafeR52Refusals.ReceiveAsync(root, caller, state,
                EventMessageSensitivePublicOperations.Receive(state.Model, Guid.NewGuid(), false), ErrorCode.Unauthenticated, token);
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(),
                state.Model.Worker with
                {
                    PolicyEpoch = EventMessageSensitiveSafeR52Protocol.RepairedEpoch,
                    FieldGrants = [EventMessageSensitivePublicProtocol.GrantAfter]
                }, token));
            await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
            await EventMessageSensitiveSafeR52Refusals.ReceiveAsync(root, caller, state, state.OriginalRequest,
                ErrorCode.PermissionDenied, token);
            await EventMessageSensitiveSafeR52Seed.PublishAsync(root, state, EventMessageSensitiveSafeR52Protocol.Second, token);
            state.FreshRequest = EventMessageSensitivePublicOperations.Receive(state.Model, Guid.NewGuid(), true);
            state.Fresh = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state.Model,
                state.FreshRequest, QueueLifecyclePublicProtocol.Routes.First(), token);
            await EventMessageSensitiveSafeR52Replay.LiteralAsync(state.Fresh, state, state.FreshId);
            state.Leased.Add(state.FreshId);
            await EventMessageSensitiveSafeR52Current.CaptureAsync(root, state, token);
            await EventMessageSensitiveSafeR52Replay.ReceiveAsync(root, caller, state, state.FreshRequest,
                state.Fresh, state.FreshId, token);
            await EventMessageSensitiveSafeR52Replay.ReceiptsAsync(root, caller, state, token);
        }, token);

    internal static async Task AfterSecondColdAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        EventMessageSensitiveSafeR52State state, CancellationToken token)
        => await ReadersAsync(fixture, state, async worker =>
        {
            var caller = state.Model.DataAuthority ? root : worker;
            await EventMessageSensitiveSafeR52Refusals.ReceiveAsync(root, caller, state, state.OriginalRequest,
                ErrorCode.PermissionDenied, token);
            await EventMessageSensitiveSafeR52Replay.ReceiveAsync(root, caller, state, state.FreshRequest,
                state.Fresh, state.FreshId, token);
            await EventMessageSensitiveSafeR52Replay.ReceiptsAsync(root, caller, state, token);
            await AckAsync(caller, state, state.Fresh, state.FreshId, token);
            state.Acknowledged.Add(state.FreshId);
            await EventMessageSensitiveSafeR52Current.CaptureAsync(root, state, token);
            await EventMessageSensitiveSafeR52Replay.ReceiptsAsync(root, caller, state, token);
            if (state.Model.Subscription)
            {
                var nextRequest = EventMessageSensitivePublicOperations.Receive(state.Model, Guid.NewGuid(), true);
                var next = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state.Model, nextRequest,
                    QueueLifecyclePublicProtocol.Routes.First(), token);
                await EventMessageSensitiveSafeR52Replay.LiteralAsync(next, state, EventMessageSensitiveSafeR52Protocol.Second);
                state.Leased.Add(EventMessageSensitiveSafeR52Protocol.Second);
                await EventMessageSensitiveSafeR52Current.CaptureAsync(root, state, token);
                await EventMessageSensitiveSafeR52Replay.ReceiveAsync(root, caller, state, nextRequest, next,
                    EventMessageSensitiveSafeR52Protocol.Second, token);
                await AckAsync(caller, state, next, EventMessageSensitiveSafeR52Protocol.Second, token);
                state.Acknowledged.Add(EventMessageSensitiveSafeR52Protocol.Second);
                await EventMessageSensitiveSafeR52Current.CaptureAsync(root, state, token);
                await EventMessageSensitiveSafeR52Replay.ReceiptsAsync(root, caller, state, token);
            }
            foreach (var route in QueueLifecyclePublicProtocol.Routes)
            {
                var request = EventMessageSensitivePublicOperations.Receive(state.Model, Guid.NewGuid(), true);
                var empty = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state.Model, request, route, token);
                if (empty is ReceiveSubscriptionResult subscription)
                {
                    await Assert.That(subscription.Deliveries).IsEmpty();
                }
                else
                {
                    await Assert.That(((ReceiveResult)empty).Deliveries).IsEmpty();
                }
                await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
            }
        }, token);

    private static async Task AckAsync(RequestCqrsClusterCallers worker, EventMessageSensitiveSafeR52State state,
        object received, string id, CancellationToken token)
    {
        object request;
        CommitReceipt receipt;
        if (received is ReceiveSubscriptionResult subscription)
        {
            var command = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Model.HealthyGroup,
                subscription.Deliveries.Single().Token, DeliveryAction.Ack);
            request = command;
            receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteSubscriptionAsync(command, token));
        }
        else
        {
            var command = new DeliveryCommand(Guid.NewGuid(), state.Model.Lane, ((ReceiveResult)received).Deliveries.Single().Token, DeliveryAction.Ack);
            request = command;
            receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CompleteAsync(command, token));
        }
        var commandId = request is SubscriptionDeliveryCommand subscriptionCommand ? subscriptionCommand.CommandId
            : ((DeliveryCommand)request).CommandId;
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Model.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan((long)EventMessageSensitiveSafeR52Protocol.EmptyCount);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        var key = state.Model.Subscription
            ? (id == EventMessageSensitiveSafeR52Protocol.First ? EventMessageSensitiveSafeR52Protocol.FirstPosition
                : EventMessageSensitiveSafeR52Protocol.SecondPosition).ToString(System.Globalization.CultureInfo.InvariantCulture) : id;
        var version = state.Model.Subscription ? EventMessageSensitiveSafeR52Protocol.FirstLeaseVersion
            : EventMessageSensitiveSafeR52Protocol.AckedVersion;
        await SqlClusterProtocol.EqualAsync<MutationReceipt[]>([new(EventMessageSensitiveSafeR52Protocol.AckKind,
            state.Model.Resource.Name, key, version)], receipt.Mutations.ToArray());
        state.Completions.Add((request, receipt));
    }

    private static async Task ReadersAsync(ClusterFixture fixture,
        EventMessageSensitiveSafeR52State state, Func<RequestCqrsClusterCallers, Task> action, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Model.Key,
            async worker => { await action(worker); return true; }, token);
}
