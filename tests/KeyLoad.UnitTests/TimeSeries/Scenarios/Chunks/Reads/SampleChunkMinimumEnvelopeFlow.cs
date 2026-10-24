using KeyLoad.Server;
using KeyLoad.UnitTests.OrleansExecution;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkMinimumEnvelopeFlow
{
    private const long RevokedEpoch = 2;
    private const long RepairedEpoch = 3;

    internal static async Task RunAsync(ConnectionScenario scenario, CancellationToken token)
    {
        var owner = scenario.Fixture.Database;
        var state = await SampleChunkMinimumEnvelopeSeed.RunAsync(owner);
        await SampleChunkMinimumEnvelopeAssertions.ObserveAsync(owner.Store,
            () => SampleChunkMinimumEnvelopeAssertions.HealthyAsync(scenario, state, token));
        foreach (var fault in SampleChunkMinimumEnvelopeIssuance.OutOfScope())
        {
            var id = Guid.NewGuid();
            var issued = SampleChunkMinimumEnvelopeIssuance.Create(scenario.Fixture.Codec, state.Reader, id, state.Request);
            var outOfScope = fault(issued);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => SampleChunkMinimumEnvelopeAssertions.ObserveAsync(owner.Store,
                () => SampleChunkMinimumEnvelopeAssertions.RefusedAsync(scenario, state, id, outOfScope, outOfScope: true, token)), failures);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkMinimumEnvelopeAssertions.ObserveAsync(owner.Store,
                () => SampleChunkMinimumEnvelopeAssertions.HealthyAsync(scenario, state, token)), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        var retainedId = Guid.NewGuid();
        var retained = SampleChunkMinimumEnvelopeIssuance.Create(scenario.Fixture.Codec, state.Reader, retainedId, state.Request);
        owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(state.Reader with
        {
            Grants = [],
            PolicyEpoch = RevokedEpoch
        })).Get<PrincipalRecord>();
        await SampleChunkMinimumEnvelopeAssertions.ObserveAsync(owner.Store,
            () => SampleChunkMinimumEnvelopeAssertions.RefusedAsync(scenario, state, retainedId, retained, outOfScope: false, token));
        owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(state.Reader with
        {
            PolicyEpoch = RepairedEpoch
        })).Get<PrincipalRecord>();
        await SampleChunkMinimumEnvelopeAssertions.ObserveAsync(owner.Store,
            () => SampleChunkMinimumEnvelopeAssertions.HealthyAsync(scenario, state, token));
        await SampleChunkMinimumEnvelopeSeed.ReplayAsync(owner, state);
        await SampleChunkMinimumEnvelopeCold.ReplayAsync(owner.Database, owner.Store, state, token);
        var cold = SampleRawIntegrityAssertions.Capture(owner.Store);
        var coldSource = new ConnectionColdSource(scenario.Fixture.Source);
        await scenario.CloseAsync(token);
        await scenario.Fixture.Hosts.StopAsync(token);
        await SampleChunkMinimumEnvelopeCold.RunAsync(coldSource, state, cold, token);
    }
}
