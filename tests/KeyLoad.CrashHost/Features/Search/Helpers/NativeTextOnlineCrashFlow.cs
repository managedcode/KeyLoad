using KeyLoad.Orleans;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextOnlineCrashFlow
{
    private const int InitialReplayPage = 0;
    internal static async Task<OnlineTextIndexMaintenanceResult> RunAsync(NativeTextOnlineCrashRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, CancellationToken token,
        Func<OnlineTextIndexMaintenanceResult, Task>? afterOriginalCommit = null)
    {
        var session = Guid.NewGuid();
        var database = runtime.Canonical.Database;
        var expiry = database.EvaluationClock.GetUtcNow().AddSeconds(
            runtime.Canonical.Options.Core.DatabaseLimits.Value.QueryDeadlineSeconds);
        var original = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.ResolveOriginal, expiry, token);
        if (original.OriginalResult is { } replay)
        { return replay; }
        _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.Capture, expiry, token);
        var state = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.Seed, expiry, token);
        await ReplayAsync(runtime, session, request, state, expiry, token);
        _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.Validate, expiry, token);
        var prepared = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.IssuePublication, expiry, token);
        var operation = prepared.IssuedPublication ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        var work = runtime.Online.RequirePublicationWork(session, request, CrashFixtureValues.Principal);
        var committed = await work.AdmitOriginal(() => runtime.Canonical.CommitIssuedAsync(operation, token));
        var result = committed.Get<OnlineTextIndexMaintenanceResult>();
        if (afterOriginalCommit is not null)
        { await afterOriginalCommit(result); }
        var reconciled = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.ReconcileCommitted, expiry, token);
        RequireEqual(result, reconciled.OriginalResult);
        await runtime.Online.AbortAsync(session);
        return result;
    }

    private static async Task ReplayAsync(NativeTextOnlineCrashRuntime runtime, Guid session,
        OnlineTextIndexMaintenanceRequest request, OnlineTextCapabilityResult state, DateTimeOffset expiry,
        CancellationToken token)
    {
        for (var number = InitialReplayPage; number < runtime.Canonical.Options.Core.TextMaintenance.Value.MaximumReplayPages; number++)
        {
            var page = runtime.Canonical.Database.ReadProjectionBatch(CrashFixtureValues.Principal,
                new(request.Consumer, ThroughSequence: state.CurrentCut!.ThroughSequence));
            var id = OnlineTextCheckpointIdentity.Create(request.CommandId, page.ThroughSequence);
            var intent = new CommitProjectionBatchRequest(id, request.Consumer, page.Token, []);
            _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.PreparePage, expiry, token, page, intent);
            _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.ApplyIntent, expiry, token);
            var work = runtime.Online.TryRequireCheckpointWork(intent, CrashFixtureValues.Principal, expiry)
                ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
            var original = await work.AdmitOriginal(async () =>
            {
                _ = await runtime.Canonical.CommitAsync<ProjectionBatchResult>(OperationKind.CommitProjectionBatch, intent, id, token);
                return runtime.Canonical.Database.ResolveOutcome(runtime.Canonical.Database.NormalizeOperation(
                    new(id, OperationKind.CommitProjectionBatch, CrashFixtureValues.Principal,
                        runtime.Canonical.Database.EvaluationClock.GetUtcNow(), System.Text.Json.JsonSerializer.Serialize(intent, JsonDefaults.Options))));
            });
            state = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.SettleCheckpoint,
                expiry, token, receipt: original.Get<ProjectionBatchResult>());
            if (!page.HasMore)
            { return; }
        }
        throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
    }

    internal static void RequireEqual<T>(T expected, T actual)
    {
        if (!NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
    }
}
