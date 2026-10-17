using ManagedCode.Communication;
using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeSuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeSuccessBodyStages
{
    internal static async Task RunAsync(NativeSuccessBodyState state, CancellationToken token)
    {
        await FirstAsync(state, token);
        await ReadAndConflictAsync(state, token);
        await NativeSuccessBodyHealthy.RunAsync(state, token);
        await state.Endpoint.RequireCountsAsync();
    }

    private static async Task FirstAsync(NativeSuccessBodyState state, CancellationToken token)
    {
        var result = await CompleteAsync(state, state.Client.CommitAsync(state.Endpoint.Original, token),
            NativeSuccessBodyStep.OriginalWrite, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(result.Problem, ErrorCode.UnknownWriteOutcome, D.WriteDetail);
        state.FirstReceipt = await DeclaredResponseEofAssertions.FirstAsync(state.Scenario, state.Principal,
            state.Endpoint.Original, state.Endpoint.OriginalReceipt, state.Placement);
        state.FirstCut = DeclaredResponseEofAssertions.Capture(state.Scenario, state.Principal, state.Endpoint.Original);
        await CurrentFirstAsync(state);
        var replay = await CompleteAsync(state, state.Client.CommitAsync(state.Endpoint.Original, token),
            NativeSuccessBodyStep.OriginalReplay, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(state.FirstReceipt, replay.Value);
        await CurrentFirstAsync(state);
    }

    private static async Task ReadAndConflictAsync(NativeSuccessBodyState state, CancellationToken token)
    {
        var reference = state.Scenario.Reference(D.Document);
        var refused = await CompleteAsync(state, state.Client.GetAsync(reference, token), NativeSuccessBodyStep.RefusedRead, token);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(refused.Problem, ErrorCode.OwnershipLost, D.ReadDetail);
        await CurrentFirstAsync(state);
        var healthy = await CompleteAsync(state, state.Client.GetAsync(reference, token), NativeSuccessBodyStep.HealthyRead, token);
        await Assert.That(healthy.IsSuccess).IsTrue();
        await DeclaredResponseEofAssertions.EqualAsync(
            new DocumentReadResult(new(reference, D.Revision, D.OriginalJson, false, [])), healthy.Value);
        await CurrentFirstAsync(state);
        var conflict = await CompleteAsync(state, state.Client.CommitAsync(state.Endpoint.Changed, token),
            NativeSuccessBodyStep.Conflict, token);
        await Assert.That(conflict.IsFailed).IsTrue();
        await Assert.That(conflict.Value).IsNull();
        await DeclaredResponseEofAssertions.ProblemAsync(conflict.Problem, ErrorCode.Conflict, P.ConflictDetail);
        await CurrentFirstAsync(state);
    }

    internal static Task CurrentFirstAsync(NativeSuccessBodyState state)
        => DeclaredResponseEofAssertions.CurrentAsync(state.Scenario, state.Principal, state.Endpoint.Original,
            state.FirstCut ?? throw new InvalidOperationException(P.Failure));

    internal static async Task<Result<T>> CompleteAsync<T>(NativeSuccessBodyState state, Task<Result<T>> actual,
        NativeSuccessBodyStep step, CancellationToken token)
    {
        _ = state.Tasks.Retain(actual);
        var result = await state.Tasks.CompleteAsync(actual);
        await state.Tasks.JoinAsync(await state.Endpoint.HandlerAsync(step, token));
        await state.Endpoint.RequireDeliveryAsync(step);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
