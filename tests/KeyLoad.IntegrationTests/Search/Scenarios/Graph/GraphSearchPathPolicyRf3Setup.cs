using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Search.GraphSearchPathPolicyRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchPathPolicyRf3Setup
{
    internal static async Task<GraphSearchPathPolicyRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, CancellationToken token)
    {
        var partition = new PartitionRef(P.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            P.Database, P.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        ResourceDefinition[] resources = [new(P.Documents, ResourceKind.Collection, P.Domain),
            new(P.Projects, ResourceKind.Collection, P.Domain), new(P.Graph, ResourceKind.Graph, P.Domain)];
        foreach (var resource in resources)
        {
            await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                Guid.NewGuid(), new(partition.TenantId, P.Database, resource), token)));
        }
        var seed = GraphSearchPathPolicyRf3Recipe.Seed(partition);
        var receipt = await CommitAsync(root, seed, GraphSearchPathPolicyRf3Recipe.SeedReceipts, token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(partition, token));
        await AtomicProducerRf3Outbox.LiteralAsync(outbox, P.SeedMutations);
        var identity = await MessagingRf3Identity.CreateAsync(fixture, partition.TenantId,
            [new(P.Database, P.Documents, Capability.Query | Capability.DocumentsRead),
             new(P.Database, P.Projects, Capability.Query | Capability.DocumentsRead),
             new(P.Database, P.Graph, Capability.GraphRead)], [], false, token);
        var state = new GraphSearchPathPolicyRf3State(partition, identity, seed, receipt, outbox);
        return await PolicyAsync(root, state, true, token);
    }

    internal static async Task<GraphSearchPathPolicyRf3State> PolicyAsync(RequestCqrsRf3Callers root,
        GraphSearchPathPolicyRf3State state, bool restricted, CancellationToken token)
    {
        var updated = state.Identity.Principal with
        {
            RestrictRows = restricted,
            OwnerId = P.Owner,
            PolicyEpoch = checked(state.Identity.Principal.PolicyEpoch + 1)
        };
        await SqlRf3Protocol.EqualAsync(updated, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), updated, token)));
        return state with
        {
            Identity = state.Identity with
            {
                Principal = updated
            }
        };
    }

    internal static async Task<GraphSearchPathPolicyRf3State> RepairOwnerAsync(RequestCqrsRf3Callers root,
        GraphSearchPathPolicyRf3State state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(P.Projects, P.Middle, P.MiddleJson, ExpectedRevision: 1L, Access: new(P.Owner))]);
        var receipt = await CommitAsync(root, command, [new("putDocument", P.Projects, P.Middle, 2L)], token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
        await AtomicProducerRf3Outbox.LiteralAsync(outbox, P.FinalMutations);
        return state with
        {
            Continuation = command,
            ContinuationReceipt = receipt,
            Outbox = outbox
        };
    }

    private static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers root, CommandRequest command,
        ImmutableArray<MutationReceipt> expectedMutations, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(P.Version, command.Partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position > 0L).IsTrue();
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation, command.Partition.AtomicPartitionId,
            receipt.Token.Position, placement.PlacementEpoch), expectedMutations, DurabilityProfile.QuorumProcessDurable);
        await NativeSubmissionColdAssertions.ReceiptAsync(receipt, expected);
        return receipt;
    }
}
