using KeyLoad.Server;
using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkSignedMinimumFlow
{
    private const long RevokedEpoch = 2;
    private const long RepairedEpoch = 3;

    internal static async Task RunAsync(ConnectionScenario scenario, CancellationToken token)
    {
        var owner = scenario.Fixture.Database;
        var state = await SampleChunkSignedMinimumSeed.RunAsync(owner);
        await SampleChunkSignedMinimumAssertions.ObserveAsync(owner.Store,
            () => SampleChunkSignedMinimumAssertions.HealthyAsync(scenario, state, token));
        foreach (var replacement in new CommitToken?[] { null, SampleChunkSignedMinimumSeed.Earlier(state) })
        {
            var id = Guid.NewGuid();
            var signed = SampleChunkSignedMinimumEnvelope.Create(scenario.Fixture.Codec, state.Reader, id, state.Request);
            var tampered = SampleChunkSignedMinimumEnvelope.ReplaceMinimum(scenario.Fixture.Codec, signed, id, replacement);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => SampleChunkSignedMinimumAssertions.ObserveAsync(owner.Store,
                () => SampleChunkSignedMinimumAssertions.RefusedAsync(scenario, state, id, tampered, mac: true, token)), failures);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkSignedMinimumAssertions.ObserveAsync(owner.Store,
                () => SampleChunkSignedMinimumAssertions.HealthyAsync(scenario, state, token)), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        var retainedId = Guid.NewGuid();
        var retained = SampleChunkSignedMinimumEnvelope.Create(scenario.Fixture.Codec, state.Reader, retainedId, state.Request);
        owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(state.Reader with
        {
            Grants = [],
            PolicyEpoch = RevokedEpoch
        })).Get<PrincipalRecord>();
        await SampleChunkSignedMinimumAssertions.ObserveAsync(owner.Store,
            () => SampleChunkSignedMinimumAssertions.RefusedAsync(scenario, state, retainedId, retained, mac: false, token));
        owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(state.Reader with
        {
            PolicyEpoch = RepairedEpoch
        })).Get<PrincipalRecord>();
        await SampleChunkSignedMinimumAssertions.ObserveAsync(owner.Store,
            () => SampleChunkSignedMinimumAssertions.HealthyAsync(scenario, state, token));
        await SampleChunkSignedMinimumSeed.ReplayAsync(owner, state);
        await SampleChunkSignedMinimumCold.ReplayAsync(owner.Database, owner.Store, state, token);
        var cold = SampleRawIntegrityAssertions.Capture(owner.Store);
        var coldSource = new ConnectionColdSource(scenario.Fixture.Source);
        await scenario.CloseAsync(token);
        await scenario.Fixture.Hosts.StopAsync(token);
        await SampleChunkSignedMinimumCold.RunAsync(coldSource, state, cold, token);
    }
}
