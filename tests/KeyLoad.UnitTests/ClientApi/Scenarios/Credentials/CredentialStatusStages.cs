using KeyLoad.Client;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.ClientApi.CredentialStatusProtocol;

namespace KeyLoad.UnitTests.ClientApi;

internal static class CredentialStatusStages
{
    internal static async Task RunAsync(CredentialStatusState state, CredentialStatusEndpoint endpoint,
        HttpClient http, CancellationToken token)
    {
        var admin = new KeyLoadClient(http, P.CallerKey, UnitClientOptions.Execution());
        var ordinary = new KeyLoadClient(http, P.OriginalKey, UnitClientOptions.Execution());
        var before = CredentialStatusAssertions.Capture(state);
        var next = checked(before.Cut.AppliedPosition + P.First);
        var unknown = await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.OriginalId, state.Original, token),
            CredentialStatusStep.OriginalBoolean, token);
        await Assert.That(unknown.IsFailed).IsTrue();
        await CredentialStatusAssertions.ProblemAsync(unknown.Problem, ErrorCode.UnknownWriteOutcome, P.UnknownDetail);
        var expected = await CredentialStatusOutcome.RequireAsync(state, state.OriginalId, state.Administrator, state.Original, next);
        await CredentialStatusDelta.RequireAsync(state, before, state.OriginalId, state.Administrator, state.Original, expected);
        await CredentialStatusAssertions.CurrentAsync(state, original: true, healthy: false);
        var first = CredentialStatusAssertions.Capture(state);
        var replay = await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.OriginalId, state.Original, token),
            CredentialStatusStep.OriginalReplay, token);
        await SuccessAsync(replay);
        await CredentialStatusAssertions.UnchangedAsync(state, first, healthy: false);
        await DeniedAndHealthyAsync(state, endpoint, admin, ordinary, token);
        await endpoint.RequireCountAsync();
    }

    private static async Task DeniedAndHealthyAsync(CredentialStatusState state,
        CredentialStatusEndpoint endpoint, KeyLoadClient admin, KeyLoadClient ordinary, CancellationToken token)
    {
        var before = CredentialStatusAssertions.Capture(state);
        await CredentialStatusAssertions.AbsentDeniedOutcomeAsync(state);
        var denied = await CompleteAsync(state, endpoint, ordinary.ConfigureApiKeyAsync(state.DeniedId, state.Denied, token),
            CredentialStatusStep.OrdinaryDenied, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await CredentialStatusAssertions.ProblemAsync(denied.Problem, ErrorCode.PermissionDenied, P.DeniedDetail);
        await CredentialStatusAssertions.UnchangedAsync(state, before, healthy: false);
        await CredentialStatusAssertions.AbsentDeniedOutcomeAsync(state);
        before = CredentialStatusAssertions.Capture(state);
        var next = checked(before.Cut.AppliedPosition + P.First);
        var healthy = await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.HealthyId, state.Healthy, token),
            CredentialStatusStep.Healthy, token);
        await SuccessAsync(healthy);
        var expected = await CredentialStatusOutcome.RequireAsync(state, state.HealthyId, state.Administrator, state.Healthy, next);
        await CredentialStatusDelta.RequireAsync(state, before, state.HealthyId, state.Administrator, state.Healthy, expected);
        await CredentialStatusAssertions.CurrentAsync(state, original: true, healthy: true);
        var current = CredentialStatusAssertions.Capture(state);
        await SuccessAsync(await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.HealthyId, state.Healthy, token),
            CredentialStatusStep.HealthyReplay, token));
        await CredentialStatusAssertions.UnchangedAsync(state, current, healthy: true);
        await SuccessAsync(await CompleteAsync(state, endpoint, admin.ConfigureApiKeyAsync(state.OriginalId, state.Original, token),
            CredentialStatusStep.OriginalFinalReplay, token));
        await CredentialStatusAssertions.UnchangedAsync(state, current, healthy: true);
    }

    private static async Task<Result> CompleteAsync(CredentialStatusState state, CredentialStatusEndpoint endpoint,
        Task<Result> actual, CredentialStatusStep step, CancellationToken token)
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
