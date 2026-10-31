using KeyLoad.Clustering;
using KeyLoad.Clustering.Search;
using KeyLoad.Core;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class TextOnlineCrashFlow
{

    internal static async Task<OnlineTextIndexMaintenanceResult> RunAsync(TextOnlineCrashRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, CancellationToken token,
        Func<OnlineTextIndexMaintenanceResult, Task>? afterOriginalCommit = null)
    {
        var session = Guid.NewGuid();
        var database = runtime.Canonical.Database;
        var original = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.ResolveOriginal, token);
        if (original.OriginalResult is { } replay)
        {
            return replay;
        }
        _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.Capture, token);
        var state = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.Seed, token);
        await ReplayAsync(runtime, session, request, state, token);
        _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.Validate, token);
        var prepared = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.IssuePublication, token);
        var operation = prepared.IssuedPublication ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        var work = runtime.Online.RequirePublicationWork(session, request, CrashFixtureValues.Principal);
        var committed = await work.AdmitOriginal(() => runtime.Canonical.CommitIssuedAsync(operation, token));
        var result = committed.Get<OnlineTextIndexMaintenanceResult>();
        if (afterOriginalCommit is not null)
        {
            await afterOriginalCommit(result);
        }
        var reconciled = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.ReconcileCommitted, token);
        RequireEqual(result, reconciled.OriginalResult);
        await runtime.Online.AbortAsync(session);
        return result;
    }

    private static async Task ReplayAsync(TextOnlineCrashRuntime runtime, Guid session,
        OnlineTextIndexMaintenanceRequest request, OnlineTextCapabilityResult state,
        CancellationToken token)
    {
        while (true)
        {
            var page = runtime.Canonical.Database.ReadProjectionBatch(CrashFixtureValues.Principal,
                new(request.Consumer, ThroughSequence: state.CurrentCut!.ThroughSequence));
            var id = OnlineTextCheckpointIdentity.Create(request.CommandId, page.ThroughSequence);
            var intent = new CommitProjectionBatchRequest(id, request.Consumer, page.Token, []);
            _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.PreparePage, token, page, intent);
            _ = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.ApplyIntent, token);
            var work = runtime.Online.TryRequireCheckpointWork(intent, CrashFixtureValues.Principal)
                ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
            var original = await work.AdmitOriginal(async () =>
            {
                _ = await runtime.Canonical.CommitAsync<ProjectionBatchResult>(OperationKind.CommitProjectionBatch, intent, id, token);
                return runtime.Canonical.Database.ResolveOutcome(runtime.Canonical.Database.OwnCommand(
                    runtime.Canonical.Database.CreateCommand(OperationKind.CommitProjectionBatch, id, CrashFixtureValues.Principal, runtime.Canonical.Database.EvaluationClock.GetUtcNow(), System.Text.Json.JsonSerializer.Serialize(intent, JsonDefaults.Options))));
            });
            state = await runtime.PhaseAsync(session, request, OnlineTextCapabilityKind.SettleCheckpoint, token, receipt: original.Get<ProjectionBatchResult>());
            if (!page.HasMore)
            {
                return;
            }
        }
    }

    internal static void RequireEqual<T>(T expected, T actual)
    {
        if (!OrleansSerialization.Serialize(expected).AsSpan().SequenceEqual(OrleansSerialization.Serialize(actual)))
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
    }
}
