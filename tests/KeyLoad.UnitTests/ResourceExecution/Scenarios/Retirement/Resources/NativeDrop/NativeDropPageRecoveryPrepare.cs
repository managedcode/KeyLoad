using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageRecoveryPrepare
{
    internal static async Task<ResourceRetirementAttemptV1> PrepareAsync(NativeDropPageAttemptState state)
    {
        var expiry = state.Context.Clock.GetUtcNow() + NativeDropPageRecoveryData.AttemptLifetime;
        if (expiry >= state.Call.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, NativeDropPageRecoveryData.MissingFailure);
        }

        return await new ResourceRetirementAttemptSource(state.Context.Phases, state.Proof,
            state.Profile.Source.Runtime.Core.DatabaseLimits).PrepareAsync(state.Scope, null, null,
                state.Context.Source, state.Context.PrincipalId, state.Context.PolicyEpoch, expiry,
                state.Context.Work, state.Context.Metadata, state.Context.Token).ConfigureAwait(false);
    }

    internal static Task<Command<DatabaseCommand>> IssueAsync(NativeDropPageAttemptState state,
        ResourceRetirementAttemptV1 attempt, bool tiny)
    {
        var body = new ResourceRetirementCleanupPageBodyV1(state.OriginalState, state.Receiver,
            state.Scope.Page!.Partition, null)
        {
            Attempt = attempt,
            MaximumReadBytes = tiny ? NativeDropPageRecoveryData.TinyReadBytes : state.Context.Work.RemainingReadGrantBytes,
            MaximumExaminedRecords = tiny ? NativeDropPageRecoveryData.TinyReadRecords : state.Context.Work.RemainingReadGrantRecords
        };
        return NativeDropTablePageAttempts.IssueAsync(state.Context, state.Scope, body);
    }

    internal static Task<ResourceRetirementPreparedOperationV1> PersistAsync(NativeDropPageAttemptState state,
        ResourceRetirementAttemptV1 attempt, Command<DatabaseCommand> original)
        => state.Operations.PersistAsync(state.Scope, attempt, original, state.Receiver, state.Context.PrincipalId,
            state.Context.PolicyEpoch, attempt.ExpiresAt, state.Context.Work, state.Context.Metadata, state.Context.Token);

    internal static Task<ResourceRetirementPreparedOperationV1> CloseAsync(NativeDropPageAttemptState state,
        ResourceRetirementAttemptV1 attempt)
        => state.Operations.CloseAsync(state.Scope, attempt, state.Receiver, state.Context.PrincipalId,
            state.Context.PolicyEpoch, state.Context.Expiry, state.Context.Work, state.Context.Metadata, state.Context.Token);

    internal static async Task ExpireAsync(NativeDropPageAttemptState state, ResourceRetirementAttemptV1 attempt)
    {
        var clock = state.Context.Clock;
        while (true)
        {
            var remaining = attempt.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining, clock, state.Context.Token).ConfigureAwait(false);
        }
    }
}
