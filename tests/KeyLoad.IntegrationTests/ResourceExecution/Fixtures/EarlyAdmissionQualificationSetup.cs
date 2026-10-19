using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class EarlyAdmissionQualificationSetup
{
    internal static async Task<EarlyAdmissionQualificationState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, Domain);
        await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, Database, resource), token)));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, First, FirstJson), new PutDocument(Collection, Second, SecondJson)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(InvalidCut);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(receipt.Token.Incarnation, partition.AtomicPartitionId, receipt.Token.Position),
            [new(Put, Collection, First, Revision), new(Put, Collection, Second, Revision)],
            DurabilityProfile.ProcessDurable, 3), receipt);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new EarlyAdmissionQualificationState(partition, command, receipt, outbox, identity);
        await EarlyAdmissionQualificationCurrent.FullAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, EarlyAdmissionQualificationState state,
        bool allowed, long epoch, CancellationToken token)
    {
        var principal = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(Database, Collection, allowed ? Capability.Query | Capability.DocumentsRead : Capability.DocumentsRead)]
        };
        await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
    }
}
