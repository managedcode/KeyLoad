using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialCredentialAuthoritySetup
{
    private const string PutKind = "putDocument";

    internal static async Task<InitialCredentialAuthorityState> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await McpDocumentScenario.CreateAsync(fixture, token);
        var command = scenario.Command(Guid.NewGuid());
        var receipt = await AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1,
            async root => await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(command, token)));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        MutationReceipt[] expected = [new(PutKind, McpDocumentProtocol.Collection,
            McpDocumentProtocol.Entity, McpCallerProtocol.FirstRevision)];
        await SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, Capability.DocumentsRead, token);
        return new(new(scenario, command, receipt), identity);
    }
}
