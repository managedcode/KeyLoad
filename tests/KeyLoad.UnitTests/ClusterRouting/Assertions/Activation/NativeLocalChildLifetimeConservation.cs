namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeConservation
{
    internal static async Task VerifyAsync(RequestCqrsHostedRuntimeFixture fixture,
        NativeLocalChildLifetimeSeeded state, CancellationToken token)
    {
        await NativeLocalChildLifetimeAssertions.RowsAsync(state, token);
        await NativeLocalChildLifetimeSeed.UnchangedAsync(fixture, state.Images[fixture]);
        await NativeLocalChildLifetimeSeed.UnchangedAsync(state.Other, state.Images[state.Other]);
        var cut = fixture.Database.ReadCanonicalCut();
        var replay = (await fixture.Database.Coordinator.SubmitNativeAsync(state.Write.Value!.Kind,
            state.Write.CommandId, state.Write.UserId!, state.Write.Value.Input, cancellationToken: token)).Get<CommitReceipt>();
        await NativeLocalChildLifetimeAssertions.EqualAsync(state.Receipt, replay);
        await Assert.That(fixture.Database.ReadCanonicalCut()).IsEqualTo(cut);
        await NativeLocalChildLifetimeSeed.UnchangedAsync(fixture, state.Images[fixture]);
        await NativeLocalChildLifetimeAssertions.RowsAsync(state, token);
    }
}
