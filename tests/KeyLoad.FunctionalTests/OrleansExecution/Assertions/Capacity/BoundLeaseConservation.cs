namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class BoundLeaseConservation
{
    internal static async Task VerifyAsync(RequestCqrsHostedRuntimeFixture fixture,
        BoundLeaseSeeded state, CancellationToken token)
    {
        await BoundLeaseAssertions.RowsAsync(state, token);
        await BoundLeaseSeed.UnchangedAsync(fixture, state.Images[fixture]);
        await BoundLeaseSeed.UnchangedAsync(state.Other, state.Images[state.Other]);
        var cut = fixture.Database.ReadCanonicalCut();
        var replay = (await fixture.Database.Coordinator.RuntimeSubmitAsync(state.Write.Value!.Kind,
            state.Write.CommandId, state.Write.UserId!, state.Write.Value.Input, cancellationToken: token)).Get<CommitReceipt>();
        await BoundLeaseAssertions.EqualAsync(state.Receipt, replay);
        await Assert.That(fixture.Database.ReadCanonicalCut()).IsEqualTo(cut);
        await BoundLeaseSeed.UnchangedAsync(fixture, state.Images[fixture]);
        await BoundLeaseAssertions.RowsAsync(state, token);
    }
}
