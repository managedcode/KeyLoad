using ManagedCode.Communication;
using D = KeyLoad.FunctionalTests.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.FunctionalTests.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SuccessBodyStages
{
    internal static async Task RunAsync(SuccessBodyState state, CancellationToken token)
    {
        await FirstAsync(state, token);
        await ReadAndConflictAsync(state, token);
        await SuccessBodyHealthy.RunAsync(state, token);
        await state.Endpoint.RequireCountsAsync();
    }

    private static async Task FirstAsync(SuccessBodyState state, CancellationToken token)
    {
        var result = await CompleteAsync(state, state.Client.CommitAsync(state.Endpoint.Original, token),
            SuccessBodyStep.OriginalWrite, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(result.Problem, ErrorCode.UnknownWriteOutcome, D.WriteDetail);
        state.FirstReceipt = await DeclaredResponseEofAssertions.FirstAsync(state.Scenario, state.Principal,
            state.Endpoint.Original, state.Endpoint.OriginalReceipt, state.Placement);
        state.FirstCut = DeclaredResponseEofAssertions.Capture(state.Scenario, state.Principal, state.Endpoint.Original);
        await CurrentFirstAsync(state);
        var replay = await CompleteAsync(state, state.Client.CommitAsync(state.Endpoint.Original, token),
            SuccessBodyStep.OriginalReplay, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(state.FirstReceipt, replay.Value);
        await CurrentFirstAsync(state);
    }

    private static async Task ReadAndConflictAsync(SuccessBodyState state, CancellationToken token)
    {
        var reference = state.Scenario.Reference(D.Document);
        var refused = await CompleteAsync(state, state.Client.GetAsync(reference, token), SuccessBodyStep.RefusedRead, token);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(refused.Problem, ErrorCode.OwnershipLost, D.ReadDetail);
        await CurrentFirstAsync(state);
        var healthy = await CompleteAsync(state, state.Client.GetAsync(reference, token), SuccessBodyStep.HealthyRead, token);
        await Assert.That(healthy.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(
            new DocumentReadResult(new(reference, D.Revision, D.OriginalJson, false, [])), healthy.Value);
        await CurrentFirstAsync(state);
        var conflict = await CompleteAsync(state, state.Client.CommitAsync(state.Endpoint.Changed, token),
            SuccessBodyStep.Conflict, token);
        await Assert.That(conflict.IsFailed).IsTrue();
        await Assert.That(conflict.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(conflict.Problem, ErrorCode.Conflict, P.ConflictDetail);
        await CurrentFirstAsync(state);
    }

    internal static Task CurrentFirstAsync(SuccessBodyState state)
        => DeclaredResponseEofAssertions.CurrentAsync(state.Scenario, state.Principal, state.Endpoint.Original,
            state.FirstCut ?? throw new InvalidOperationException(P.Failure));

    internal static async Task<Result<T>> CompleteAsync<T>(SuccessBodyState state, Task<Result<T>> actual,
        SuccessBodyStep step, CancellationToken token)
    {
        _ = state.Tasks.Retain(actual);
        var result = await state.Tasks.CompleteAsync(actual);
        await state.Tasks.JoinAsync(await state.Endpoint.HandlerAsync(step, token));
        await state.Endpoint.RequireDeliveryAsync(step);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
