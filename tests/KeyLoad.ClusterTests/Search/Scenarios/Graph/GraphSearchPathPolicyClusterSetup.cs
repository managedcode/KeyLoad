using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.Messaging;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.ResourceExecution;
using KeyLoad.ClusterTests.StorageReplication;
using P = KeyLoad.ClusterTests.Search.GraphSearchPathPolicyClusterProtocol;

namespace KeyLoad.ClusterTests.Search;

internal static class GraphSearchPathPolicyClusterSetup
{
    internal static async Task<GraphSearchPathPolicyClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(P.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            P.Database, P.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        ResourceDefinition[] resources = [new(P.Documents, ResourceKind.Collection, P.Domain),
            new(P.Projects, ResourceKind.Collection, P.Domain), new(P.Graph, ResourceKind.Graph, P.Domain)];
        var lifetimes = ImmutableDictionary.CreateBuilder<string, EntityRefLifetime>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var creationId = Guid.NewGuid();
            var creation = new ConfigureResourceRequest(partition.TenantId, P.Database, resource);
            await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                creationId, creation, token)));
            lifetimes.Add(resource.Name, ResourceCreationLifetime.Create(creationId, creation));
        }
        var seed = GraphSearchPathPolicyClusterRecipe.Seed(partition);
        var receipt = await CommitAsync(root, seed, GraphSearchPathPolicyClusterRecipe.SeedReceipts, token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(partition, token));
        await AtomicProducerClusterOutbox.LiteralAsync(outbox, P.SeedMutations);
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, partition.TenantId,
            [new(P.Database, P.Documents, Capability.Query | Capability.DocumentsRead),
             new(P.Database, P.Projects, Capability.Query | Capability.DocumentsRead),
             new(P.Database, P.Graph, Capability.GraphRead)], [], false, token);
        var state = new GraphSearchPathPolicyClusterState(partition, identity, seed, receipt, outbox, lifetimes.ToImmutable());
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
        await SubmissionColdAssertions.ReceiptAsync(receipt, expected);
        return receipt;
    }
}
