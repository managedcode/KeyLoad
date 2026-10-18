using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SensitiveAuthorityContinuation
{
    internal static async Task<SensitiveAuthorityState> RunAsync(KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, McpDocumentScenario scenario, McpPersistedIdentity identity,
        CommandRequest denied, CancellationToken token)
    {
        var repaired = identity.Principal with
        {
            PolicyEpoch = SensitiveAuthorityProtocol.RepairedEpoch,
            FieldGrants = [SensitiveAuthorityProtocol.RawRead, SensitiveAuthorityProtocol.RawWrite]
        };
        await SqlClusterProtocol.EqualAsync(repaired, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await SensitiveAuthorityAssertions.HealthyAsync(reader, session, scenario, false, token);
        await SensitiveAuthorityRefusals.PatchAsync(root, reader, session, scenario, identity, denied, false, token);
        var command = Command(scenario);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.CommitAsync(command, token));
        MutationReceipt[] expected = [new(SensitiveAuthorityProtocol.PatchKindName, McpDocumentProtocol.Collection,
            McpDocumentProtocol.Entity, SensitiveAuthorityProtocol.PatchedRevision)];
        await SqlClusterProtocol.EqualAsync(expected, receipt.Mutations.ToArray());
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        var state = new SensitiveAuthorityState(scenario, identity, denied, command, receipt);
        await CurrentAsync(root, reader, session, state, token);
        return state;
    }

    internal static CommandRequest Command(McpDocumentScenario scenario) => new(Guid.NewGuid(), scenario.Partition,
        [new PatchDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity,
            [new(McpDocumentProtocol.ProtectedPath, PatchKind.Set, McpDocumentProtocol.ProtectedPatchJson)],
            McpCallerProtocol.FirstRevision)]);

    internal static async Task CurrentAsync(KeyLoadClient root, KeyLoadClient reader, McpOfficialClient session,
        SensitiveAuthorityState state, CancellationToken token)
    {
        await SensitiveAuthorityAssertions.HealthyAsync(reader, session, state.Scenario, true, token);
        var before = await SensitiveAuthorityAssertions.ImageAsync(root, state.Scenario, true, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await reader.CommitAsync(state.Healthy, token)));
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(
            McpCallerTools.DocumentsCommit, state.Healthy, token));
        await SqlClusterProtocol.EqualAsync(state.Receipt, replay.Value);
        await SqlClusterProtocol.EqualAsync(before,
            await SensitiveAuthorityAssertions.ImageAsync(root, state.Scenario, true, token));
        await SensitiveAuthorityRefusals.PatchAsync(root, reader, session, state.Scenario, state.Identity,
            state.Denied, true, token);
    }
}
