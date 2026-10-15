using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeSuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeSuccessBodyHealthy
{
    internal static async Task RunAsync(NativeSuccessBodyState state, CancellationToken token)
    {
        var placement = state.Scenario.Fixture.Database.Database.ReadAtomicPartitionPlacement(D.Root,
            new(D.PlacementVersion, state.Endpoint.Healthy.Partition));
        var result = await NativeSuccessBodyStages.CompleteAsync(state,
            state.Client.CommitAsync(state.Endpoint.Healthy, token), NativeSuccessBodyStep.HealthyWrite, token);
        await Assert.That(result.IsSuccess).IsTrue();
        var expected = await NativeSuccessBodyAssertions.SecondAsync(state, placement, result.Value);
        var cut = DeclaredResponseEofAssertions.Capture(state.Scenario, state.Principal, state.Endpoint.Healthy);
        await NativeSuccessBodyAssertions.CurrentSecondAsync(state, cut);
        var replay = await NativeSuccessBodyStages.CompleteAsync(state,
            state.Client.CommitAsync(state.Endpoint.Healthy, token), NativeSuccessBodyStep.HealthyReplay, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(expected, replay.Value);
        await NativeSuccessBodyAssertions.CurrentSecondAsync(state, cut);
        var read = await NativeSuccessBodyStages.CompleteAsync(state,
            state.Client.GetAsync(state.Scenario.Reference(D.Document), token), NativeSuccessBodyStep.FinalRead, token);
        await Assert.That(read.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(new DocumentReadResult(new(
            state.Scenario.Reference(D.Document), P.HealthyRevision, P.HealthyJson, false, [])), read.Value);
        await NativeSuccessBodyAssertions.CurrentSecondAsync(state, cut);
    }
}
