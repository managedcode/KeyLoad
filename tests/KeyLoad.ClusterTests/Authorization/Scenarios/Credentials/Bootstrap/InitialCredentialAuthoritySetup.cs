using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

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
        await SqlClusterProtocol.EqualAsync(expected, receipt.Mutations.ToArray());
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, Capability.DocumentsRead, token);
        return new(new(scenario, command, receipt), identity);
    }
}
