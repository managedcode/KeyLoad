using D = KeyLoad.FunctionalTests.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.FunctionalTests.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SuccessBodyHealthy
{
    internal static async Task RunAsync(SuccessBodyState state, CancellationToken token)
    {
        var placement = state.Scenario.Fixture.Database.Database.ReadAtomicPartitionPlacement(D.Root,
            state.Endpoint.Healthy.Partition);
        var result = await SuccessBodyStages.CompleteAsync(state,
            state.Client.CommitAsync(state.Endpoint.Healthy, token), SuccessBodyStep.HealthyWrite, token);
        await Assert.That(result.IsSuccess).IsTrue();
        var expected = await SuccessBodyAssertions.SecondAsync(state, placement, result.Value);
        var cut = DeclaredResponseEofAssertions.Capture(state.Scenario, state.Principal, state.Endpoint.Healthy);
        await SuccessBodyAssertions.CurrentSecondAsync(state, cut);
        var replay = await SuccessBodyStages.CompleteAsync(state,
            state.Client.CommitAsync(state.Endpoint.Healthy, token), SuccessBodyStep.HealthyReplay, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(expected, replay.Value);
        await SuccessBodyAssertions.CurrentSecondAsync(state, cut);
        var read = await SuccessBodyStages.CompleteAsync(state,
            state.Client.GetAsync(state.Scenario.Reference(D.Document), token), SuccessBodyStep.FinalRead, token);
        await Assert.That(read.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(new DocumentReadResult(new(
            state.Scenario.Reference(D.Document), P.HealthyRevision, P.HealthyJson, false, [])), read.Value);
        await SuccessBodyAssertions.CurrentSecondAsync(state, cut);
    }
}
