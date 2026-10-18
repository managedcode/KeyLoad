using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Search.GraphSearchPathPolicyClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchPathPolicyClusterSetup
{
    internal static async Task<GraphSearchPathPolicyClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(P.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            P.Database, P.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        ResourceDefinition[] resources = [new(P.Documents, ResourceKind.Collection, P.Domain),
            new(P.Projects, ResourceKind.Collection, P.Domain), new(P.Graph, ResourceKind.Graph, P.Domain)];
        foreach (var resource in resources)
        {
            await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                Guid.NewGuid(), new(partition.TenantId, P.Database, resource), token)));
        }
        var seed = GraphSearchPathPolicyClusterRecipe.Seed(partition);
        var receipt = await CommitAsync(root, seed, GraphSearchPathPolicyClusterRecipe.SeedReceipts, token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(partition, token));
        await AtomicProducerClusterOutbox.LiteralAsync(outbox, P.SeedMutations);
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, partition.TenantId,
            [new(P.Database, P.Documents, Capability.Query | Capability.DocumentsRead),
             new(P.Database, P.Projects, Capability.Query | Capability.DocumentsRead),
             new(P.Database, P.Graph, Capability.GraphRead)], [], false, token);
        var state = new GraphSearchPathPolicyClusterState(partition, identity, seed, receipt, outbox);
        return await PolicyAsync(root, state, true, token);
    }

    internal static async Task<GraphSearchPathPolicyClusterState> PolicyAsync(RequestCqrsClusterCallers root,
        GraphSearchPathPolicyClusterState state, bool restricted, CancellationToken token)
    {
        var updated = state.Identity.Principal with
        {
            RestrictRows = restricted,
            OwnerId = P.Owner,
            PolicyEpoch = checked(state.Identity.Principal.PolicyEpoch + 1)
        };
        await SqlClusterProtocol.EqualAsync(updated, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), updated, token)));
        return state with
        {
            Identity = state.Identity with
            {
                Principal = updated
            }
        };
    }

    internal static async Task<GraphSearchPathPolicyClusterState> RepairOwnerAsync(RequestCqrsClusterCallers root,
        GraphSearchPathPolicyClusterState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(P.Projects, P.Middle, P.MiddleJson, ExpectedRevision: 1L, Access: new(P.Owner))]);
        var receipt = await CommitAsync(root, command, [new("putDocument", P.Projects, P.Middle, 2L)], token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
        await AtomicProducerClusterOutbox.LiteralAsync(outbox, P.FinalMutations);
        return state with
        {
            Continuation = command,
            ContinuationReceipt = receipt,
            Outbox = outbox
        };
    }

    private static async Task<CommitReceipt> CommitAsync(RequestCqrsClusterCallers root, CommandRequest command,
        ImmutableArray<MutationReceipt> expectedMutations, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position > 0L).IsTrue();
        var expected = new CommitReceipt(command.CommandId, new(receipt.Token.Incarnation, command.Partition.AtomicPartitionId,
            receipt.Token.Position), expectedMutations, DurabilityProfile.ProcessDurable, 3);
        await NativeSubmissionColdAssertions.ReceiptAsync(receipt, expected);
        return receipt;
    }
}
