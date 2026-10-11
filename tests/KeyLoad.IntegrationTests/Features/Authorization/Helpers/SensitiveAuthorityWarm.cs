using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SensitiveAuthorityWarm
{
    internal static async Task<SensitiveAuthorityState> RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await McpDocumentScenario.CreateProtectedAsync(fixture, token);
        await scenario.SeedAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, token);
        return await SensitiveAuthorityClients.RunAsync(fixture, identity.Secret,
            async (root, sdk, session) =>
            {
                var before = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
                    McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), token));
                var httpBefore = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(scenario.Reference, token));
                await Assert.That(before.Value.Redacted).IsTrue();
                await Assert.That(before.Value.RedactedFields).Contains(McpDocumentProtocol.ProtectedPath);
                await Assert.That(before.Value.Json.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
                await Assert.That(JsonDefaults.Serialize(before.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(httpBefore))).IsTrue();
                await SensitiveAuthorityAssertions.RedactedAsync(scenario, before.Value);
                var denied = SensitiveAuthorityContinuation.Command(scenario);
                await SensitiveAuthorityRefusals.PatchAsync(root, sdk, session, scenario, identity, denied, false, token);
                var after = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
                    McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), token));
                await Assert.That(JsonDefaults.Serialize(before.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(after.Value))).IsTrue();
                var original = await PublicReadAssertions.DocumentAsync(await root.GetAsync(scenario.Reference, token));
                await Assert.That(original!.Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
                await Assert.That(original.Json.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsTrue();
                await SensitiveAuthorityAssertions.RawAsync(scenario, original, false);
                return await SensitiveAuthorityContinuation.RunAsync(root, sdk, session, scenario, identity, denied, token);
            }, token);
    }
}
