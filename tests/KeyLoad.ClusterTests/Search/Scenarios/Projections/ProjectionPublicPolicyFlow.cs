using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Search;

internal static class ProjectionPublicPolicyFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, EventProjectionClusterScenario scenario,
        McpPersistedIdentity reader, CommitReceipt original, CancellationToken token)
    {
        await EventProjectionClusterAssertions.AssertReceiptAsync(original, scenario);
        var repaired = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            fixture.AdminKey, root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App,
                McpCallerProtocol.Node2, reader.Secret,
                caller => RevokeRepairAsync(fixture, root.Sdk, caller, scenario, reader, token), token), token);
        await ProjectionPublicPolicyCold.ReplayAsync(fixture, scenario, original, token);
        await ProjectionPublicPolicyCold.RunAsync(fixture, scenario, repaired, original, token);
        await ProjectionPublicPolicyCold.RunAsync(fixture, scenario, repaired, original, token);
    }

    private static async Task<McpPersistedIdentity> RevokeRepairAsync(ClusterFixture fixture, global::KeyLoad.Client.KeyLoadClient root,
        RequestCqrsClusterCallers caller, EventProjectionClusterScenario scenario,
        McpPersistedIdentity reader, CancellationToken token)
    {
        var revoked = reader.Principal with
        {
            Revoked = true,
            PolicyEpoch = checked(reader.Principal.PolicyEpoch + 1)
        };
        await PersistAsync(root, revoked, token);
        var before = await ProjectionPublicPolicyImage.ReadAsync(root, scenario, token);
        await EventProjectionClusterAssertions.AssertSdkFailureAsync(
            await caller.Sdk.SearchAsync(scenario.VectorSearch(), token), ErrorCode.Unauthenticated);
        await RefuseMcpAsync(fixture, caller, scenario, reader, token);
        var after = await ProjectionPublicPolicyImage.ReadAsync(root, scenario, token);
        await Assert.That(after.CutPosition).IsEqualTo(before.CutPosition);
        var repaired = revoked with
        {
            Revoked = false,
            RestrictRows = false,
            OwnerId = null,
            PolicyEpoch = checked(revoked.PolicyEpoch + 1)
        };
        await PersistAsync(root, repaired, token);
        await EventProjectionClusterAssertions.VerifyVectorBothAsync(caller.Sdk, caller.Mcp,
            scenario, reader.Secret, ProjectionPublicPolicyProtocol.Healthy(), token);
        await ProjectionPublicPolicyImage.ReadAsync(root, scenario, token);
        return reader with
        {
            Principal = repaired
        };
    }

    private static async Task RefuseMcpAsync(ClusterFixture fixture, RequestCqrsClusterCallers caller,
        EventProjectionClusterScenario scenario, McpPersistedIdentity reader, CancellationToken token)
    {
        var error = await Assert.ThrowsAsync<global::System.Net.Http.HttpRequestException>(
            () => caller.Mcp.CallAsync(McpCallerTools.SearchExecute, scenario.VectorSearch(), token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(global::System.Net.HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(reader.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(error.Message.Contains(EventProjectionClusterScenario.Secret, StringComparison.Ordinal)).IsFalse();
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, reader.Secret, token);
    }

    private static async Task PersistAsync(global::KeyLoad.Client.KeyLoadClient root, global::KeyLoad.PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await Assert.That(global::KeyLoad.JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(expected))).IsTrue();
    }
}
