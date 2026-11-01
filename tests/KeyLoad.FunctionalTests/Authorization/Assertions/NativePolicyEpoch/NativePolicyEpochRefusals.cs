using KeyLoad.FunctionalTests.Messaging;
using P = KeyLoad.FunctionalTests.Authorization.NativePolicyEpochProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class NativePolicyEpochRefusals
{
    internal static async Task DeniedAsync(NativePolicyEpochState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            state.Database.GetDocument(P.Reader, state.Reference, cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Message).IsEqualTo(P.ScopeDenied);
        await NativePolicyEpochAssertions.UnchangedAsync(state, image, position);
    }

    internal static async Task CancelAsync(NativePolicyEpochState state)
    {
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        using var selected = new CancellationTokenSource();
        selected.Cancel();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            state.Database.GetDocument(P.Reader, state.Reference, cancellationToken: selected.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(selected.Token);
        await NativePolicyEpochAssertions.UnchangedAsync(state, image, position);
    }

    internal static async Task ReplayAsync(NativePolicyEpochState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        foreach (var pair in new[] { (state.Seed, state.SeedReceipt, state.SeedOutcome),
            (state.Healthy, state.HealthyReceipt, state.HealthyOutcome) })
        {
            var actual = state.Submit(OperationKind.Batch, pair.Item1, token, pair.Item1.CommandId).Get<CommitReceipt>();
            await NativePolicyEpochAssertions.EqualAsync(pair.Item2, actual);
            await Assert.That(actual.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
            await Assert.That(state.Outcome(pair.Item1).AsSpan().SequenceEqual(pair.Item3)).IsTrue();
            await NativePolicyEpochAssertions.UnchangedAsync(state, image, position);
        }
        await NativePolicyEpochAssertions.ReadAsync(state, true, true, token);
    }
}
