using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Assertions;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkJobRevocationHeld
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, SampleChunkJobRevocationScenario scenario,
        KeyLoadClient administrator, CancellationToken token)
    {
        var controls = wave.QueryControls;
        var discovery = await DiscoveryAsync(wave, token).ConfigureAwait(false);
        var command = SampleChunkJobProbeIdentity.OriginalMerge(scenario);
        var arm = controls.WriteArm(scenario.Creator.Id, command, null,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        try
        {
            var correction = await scenario.Window.CommitAsync(administrator,
                new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                    [scenario.Window.Late], TimeSeriesRf3Scenario.PrivateTags), token).ConfigureAwait(false);
            scenario.Correction = correction;
            var held = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
                RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
            await Assert.That(held.CommandId).IsEqualTo(command);
            var revoked = scenario.Creator with
            {
                Grants = [],
                PolicyEpoch = scenario.Creator.PolicyEpoch + SampleChunkJobRevocationProtocol.NextPolicy
            };
            var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
                Guid.NewGuid(), revoked, token).ConfigureAwait(false));
            await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
                .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(revoked)));
            await SampleChunkJobRevocationAssertions.UnchangedAsync(scenario, administrator, token);
            controls.WriteRelease(arm, held.RequestId);
            await RequestCqrsAuthorityFaultLifecycleAssertions.VerifyReleasedAndDisposedAsync(
                controls, discovery, arm, command, held, token).ConfigureAwait(false);
            await controls.RetireArmAsync(arm, token).ConfigureAwait(false);
            await SampleChunkJobRevocationAssertions.UnchangedAsync(scenario, administrator, token);
            var restored = scenario.Creator with
            {
                PolicyEpoch = actual.PolicyEpoch
                + SampleChunkJobRevocationProtocol.NextPolicy
            };
            await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
                Guid.NewGuid(), restored, token).ConfigureAwait(false));
            var merged = await scenario.Window.WaitForActualMergeAsync(administrator, token).ConfigureAwait(false);
            await SampleChunkRf3Assertions.CompleteAsync(scenario.Window, merged, correction);
        }
        catch (Exception primary)
        {
            await CleanupAsync(controls, discovery, primary, token).ConfigureAwait(false);
            throw;
        }
        await CleanupAsync(controls, discovery, null, token).ConfigureAwait(false);
    }

    private static async Task CleanupAsync(RequestCqrsProbeFixture controls,
        IReadOnlyList<KeyLoad.Orleans.ReplicaSiloDiscovery> discovery, Exception? primary,
        CancellationToken token)
    {
        try
        { await controls.ReleaseOpenArmsAsync(discovery, token).ConfigureAwait(false); }
        catch (Exception cleanup)
        {
            if (primary is not null)
            { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private static async Task<IReadOnlyList<KeyLoad.Orleans.ReplicaSiloDiscovery>> DiscoveryAsync(
        TwoRf3MembershipWave wave, CancellationToken token)
    {
        var result = new List<KeyLoad.Orleans.ReplicaSiloDiscovery>();
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2,
            RequestCqrsRf3Protocol.Node3 })
        {
            result.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
            node, wave.Profile, token).ConfigureAwait(false));
        }
        return result;
    }
}
