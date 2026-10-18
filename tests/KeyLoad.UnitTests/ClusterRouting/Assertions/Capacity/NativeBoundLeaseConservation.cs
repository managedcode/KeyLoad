namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseConservation
{
    internal static async Task VerifyAsync(RequestCqrsHostedRuntimeFixture fixture,
        NativeBoundLeaseSeeded state, CancellationToken token)
    {
        await NativeBoundLeaseAssertions.RowsAsync(state, token);
        await NativeBoundLeaseSeed.UnchangedAsync(fixture, state.Images[fixture]);
        await NativeBoundLeaseSeed.UnchangedAsync(state.Other, state.Images[state.Other]);
        var cut = fixture.Database.ReadCanonicalCut();
        var replay = (await fixture.Database.Coordinator.SubmitNativeAsync(state.Write.Value!.Kind,
            state.Write.CommandId, state.Write.UserId!, state.Write.Value.Input, cancellationToken: token)).Get<CommitReceipt>();
        await NativeBoundLeaseAssertions.EqualAsync(state.Receipt, replay);
        await Assert.That(fixture.Database.ReadCanonicalCut()).IsEqualTo(cut);
        await NativeBoundLeaseSeed.UnchangedAsync(fixture, state.Images[fixture]);
        await NativeBoundLeaseAssertions.RowsAsync(state, token);
    }
}
