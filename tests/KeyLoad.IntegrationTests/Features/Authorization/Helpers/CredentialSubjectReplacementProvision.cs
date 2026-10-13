using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialSubjectReplacementProvision
{
    private const string ColdPrefix = "credential-subject-replacement-";

    internal static async Task<CredentialSubjectReplacementState> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var first = await CreateDocumentAsync(fixture, token);
        var second = await CreateDocumentAsync(fixture, token);
        await Assert.That(first.Scenario.Partition.TenantId).IsNotEqualTo(second.Scenario.Partition.TenantId);
        await Assert.That(first.Identity.Principal.Id).IsNotEqualTo(second.Identity.Principal.Id);
        return await AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1, async root =>
        {
            var firstCommand = first.Scenario.Command(Guid.NewGuid());
            var secondCommand = second.Scenario.Command(Guid.NewGuid());
            var firstReceipt = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(firstCommand, token));
            var secondReceipt = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(secondCommand, token));
            var state = new CredentialSubjectReplacementState(first, second, firstCommand, firstReceipt,
                secondCommand, secondReceipt, Guid.NewGuid(), Guid.NewGuid(),
                ColdPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            _ = await CredentialSubjectReplacementAssertions.ImageAsync(root, state, token);
            return state;
        });
    }

    private static async Task<DiscoveryAuthorityState> CreateDocumentAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await McpDocumentScenario.CreateAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, Capability.DocumentsRead, token);
        return new(scenario, identity, Guid.NewGuid(), identity.Principal);
    }
}
