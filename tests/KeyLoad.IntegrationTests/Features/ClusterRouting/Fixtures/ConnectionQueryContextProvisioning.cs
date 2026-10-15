using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextProvisioning
{
    internal static async Task<ConnectionQueryContextState> RunAsync(ConnectionRf3Scenario scenario, CancellationToken token)
        => new(await SeedAsync(scenario, scenario.Identity, ConnectionQueryContextRequests.FirstJson, token),
            await SeedAsync(scenario, scenario.SecondIdentity, ConnectionQueryContextRequests.SecondJson, token));
    private static async Task<ConnectionQueryContextSeed> SeedAsync(ConnectionRf3Scenario scenario,
        RequestCqrsPhaseFaultIdentity identity, string json, CancellationToken token)
    {
        await PolicyAsync(scenario, identity, true, 2L, token);
        var owner = await McpCallerAssertions.SdkSuccessAsync(await scenario.Administrator.ReadAtomicPartitionPlacementAsync(
            new(1, identity.Partition), token));
        await SqlRf3Protocol.EqualAsync(identity.Partition, owner.Partition);
        var command = new CommandRequest(Guid.NewGuid(), identity.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, ConnectionQueryContextRequests.AddedId, json, ExpectedRevision: 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await scenario.Administrator.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(owner.Incarnation, identity.Partition.AtomicPartitionId, receipt.Token.Position, owner.PlacementEpoch),
            [new MutationReceipt("putDocument", RequestCqrsRf3Protocol.AdminCollection, ConnectionQueryContextRequests.AddedId, 1L)],
            DurabilityProfile.QuorumProcessDurable), receipt);
        return new(identity, json, command, receipt,
            await McpCallerAssertions.SdkSuccessAsync(await scenario.Administrator.OutboxStatusAsync(identity.Partition, token)));
    }
    internal static async Task PolicyAsync(ConnectionRf3Scenario scenario, RequestCqrsPhaseFaultIdentity identity,
        bool query, long epoch, CancellationToken token)
    {
        var expected = new PrincipalRecord(identity.PrincipalId, identity.Partition.TenantId,
            [new(identity.Partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite | (query ? Capability.Query : Capability.None))], [])
        {
            PolicyEpoch = epoch
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await scenario.Administrator.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
