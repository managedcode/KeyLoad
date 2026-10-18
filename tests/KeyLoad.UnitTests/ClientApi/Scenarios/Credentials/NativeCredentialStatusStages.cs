using KeyLoad.Client;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeCredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeCredentialStatusStages
{
    internal static async Task RunAsync(NativeCredentialStatusState state, NativeCredentialStatusEndpoint endpoint,
        HttpClient http, CancellationToken token)
    {
        var admin = new KeyLoadClient(http, P.CallerKey, UnitClientOptions.Execution());
        var ordinary = new KeyLoadClient(http, P.OriginalKey, UnitClientOptions.Execution());
        var before = NativeCredentialStatusAssertions.Capture(state);
        var next = checked(before.Cut.AppliedPosition + P.First);
        var unknown = await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.OriginalId, state.Original, token),
            NativeCredentialStatusStep.OriginalBoolean, token);
        await Assert.That(unknown.IsFailed).IsTrue();
        await NativeCredentialStatusAssertions.ProblemAsync(unknown.Problem, ErrorCode.UnknownWriteOutcome, P.UnknownDetail);
        var expected = await NativeCredentialStatusOutcome.RequireAsync(state, state.OriginalId, state.Administrator, state.Original, next);
        await NativeCredentialStatusDelta.RequireAsync(state, before, state.OriginalId, state.Administrator, state.Original, expected);
        await NativeCredentialStatusAssertions.CurrentAsync(state, original: true, healthy: false);
        var first = NativeCredentialStatusAssertions.Capture(state);
        var replay = await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.OriginalId, state.Original, token),
            NativeCredentialStatusStep.OriginalReplay, token);
        await SuccessAsync(replay);
        await NativeCredentialStatusAssertions.UnchangedAsync(state, first, healthy: false);
        await DeniedAndHealthyAsync(state, endpoint, admin, ordinary, token);
        await endpoint.RequireCountAsync();
    }

    private static async Task DeniedAndHealthyAsync(NativeCredentialStatusState state,
        NativeCredentialStatusEndpoint endpoint, KeyLoadClient admin, KeyLoadClient ordinary, CancellationToken token)
    {
        var before = NativeCredentialStatusAssertions.Capture(state);
        await NativeCredentialStatusAssertions.AbsentDeniedOutcomeAsync(state);
        var denied = await CompleteAsync(state, endpoint, ordinary.ConfigureApiKeyAsync(state.DeniedId, state.Denied, token),
            NativeCredentialStatusStep.OrdinaryDenied, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await NativeCredentialStatusAssertions.ProblemAsync(denied.Problem, ErrorCode.PermissionDenied, P.DeniedDetail);
        await NativeCredentialStatusAssertions.UnchangedAsync(state, before, healthy: false);
        await NativeCredentialStatusAssertions.AbsentDeniedOutcomeAsync(state);
        before = NativeCredentialStatusAssertions.Capture(state);
        var next = checked(before.Cut.AppliedPosition + P.First);
        var healthy = await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.HealthyId, state.Healthy, token),
            NativeCredentialStatusStep.Healthy, token);
        await SuccessAsync(healthy);
        var expected = await NativeCredentialStatusOutcome.RequireAsync(state, state.HealthyId, state.Administrator, state.Healthy, next);
        await NativeCredentialStatusDelta.RequireAsync(state, before, state.HealthyId, state.Administrator, state.Healthy, expected);
        await NativeCredentialStatusAssertions.CurrentAsync(state, original: true, healthy: true);
        var current = NativeCredentialStatusAssertions.Capture(state);
        await SuccessAsync(await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.HealthyId, state.Healthy, token),
            NativeCredentialStatusStep.HealthyReplay, token));
        await NativeCredentialStatusAssertions.UnchangedAsync(state, current, healthy: true);
        await SuccessAsync(await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.OriginalId, state.Original, token),
            NativeCredentialStatusStep.OriginalFinalReplay, token));
        await NativeCredentialStatusAssertions.UnchangedAsync(state, current, healthy: true);
    }

    private static async Task<Result> CompleteAsync(NativeCredentialStatusState state, NativeCredentialStatusEndpoint endpoint,
        Task<Result> actual, NativeCredentialStatusStep step, CancellationToken token)
    {
        _ = state.Tasks.Retain(actual);
        var result = await state.Tasks.CompleteAsync(actual);
        await endpoint.JoinAsync(step, token);
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static async Task SuccessAsync(Result actual)
    {
        await Assert.That(actual.IsSuccess).IsTrue();
        await Assert.That(actual.Problem).IsNull();
    }
}
