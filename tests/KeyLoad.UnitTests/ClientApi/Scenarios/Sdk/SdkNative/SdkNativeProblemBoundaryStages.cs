using KeyLoad.Client;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.Features.ClientApi.SdkNativeProblemBoundaryProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkNativeProblemBoundaryStages
{
    internal static async Task RunAsync(SdkNativeProblemBoundaryScenario state, KeyLoadClientKestrelServer server,
        SdkNativeProblemBoundaryTasks tasks, bool declared, CancellationToken token)
    {
        var original = SdkNativeProblemBoundaryAssertions.Capture(state);
        await SdkNativeProblemBoundaryWire.RequireAsync(server, state, tasks, declared, token);
        await SdkNativeProblemBoundaryAssertions.SameAsync(state, original);
        foreach (var cap in new[] { SdkNativeProblemBoundaryScenario.ProblemBytes - P.Lookahead, SdkNativeProblemBoundaryScenario.ProblemBytes })
        {
            var sdk = new KeyLoadClient(server.Client, P.Secret, UnitClientOptions.Execution(new()
            {
                MaximumProblemBodyBytes = cap
            }));
            await FailureAsync(await tasks.CompleteAsync(tasks.Retain(sdk.GetAsync(state.Forbidden, token))),
                cap, write: false);
            await SdkNativeProblemBoundaryAssertions.SameAsync(state, original);
            await FailureAsync(await tasks.CompleteAsync(tasks.Retain(sdk.CommitAsync(state.Denied, token))),
                cap, write: true);
            await SdkNativeProblemBoundaryAssertions.SameAsync(state, original);
        }
        var healthy = new KeyLoadClient(server.Client, P.Secret, UnitClientOptions.Execution());
        var read = await tasks.CompleteAsync(tasks.Retain(healthy.GetAsync(state.Allowed, token)));
        await Assert.That(read.IsSuccess).IsTrue();
        await SdkNativeProblemBoundaryAssertions.EqualAsync(new DocumentReadResult(
            new(state.Allowed, P.FirstRevision, P.SeedJson, false, [])), read.Value!);
        await SdkNativeProblemBoundaryAssertions.SameAsync(state, original);
        state.HealthyToken = ExpectedCanonicalCommit.Next(state.Native.Fixture.Database.Database, state.Healthy.Partition);
        var committed = await tasks.CompleteAsync(tasks.Retain(healthy.CommitAsync(state.Healthy, token)));
        await Assert.That(committed.IsSuccess).IsTrue();
        state.HealthyReceipt = committed.Value ?? throw new InvalidOperationException(P.Failure);
        await SdkNativeProblemBoundaryAssertions.ReceiptAsync(state, state.Healthy, state.HealthyReceipt, root: false);
        await SdkNativeProblemBoundaryAssertions.CurrentAsync(state);
        var after = SdkNativeProblemBoundaryAssertions.Capture(state);
        var replay = await tasks.CompleteAsync(tasks.Retain(healthy.CommitAsync(state.Healthy, token)));
        await Assert.That(replay.IsSuccess).IsTrue();
        await SdkNativeProblemBoundaryAssertions.EqualAsync(state.HealthyReceipt, replay.Value!);
        await SdkNativeProblemBoundaryAssertions.SameAsync(state, after);
        var reply = await state.ReplaySeedAsync(token);
        await Assert.That(reply.Error).IsNull();
        await SdkNativeProblemBoundaryAssertions.EqualAsync(state.SeedReceipt, ConnectionNativeAssertions.Value<CommitReceipt>(reply));
        await SdkNativeProblemBoundaryAssertions.SameAsync(state, after);
        var current = await tasks.CompleteAsync(tasks.Retain(healthy.GetAsync(state.Allowed, token)));
        await Assert.That(current.IsSuccess).IsTrue();
        await SdkNativeProblemBoundaryAssertions.EqualAsync(new DocumentReadResult(
            new(state.Allowed, P.HealthyRevision, P.HealthyJson, false, [])), current.Value!);
        await SdkNativeProblemBoundaryAssertions.SameAsync(state, after);
    }
    private static async Task FailureAsync<T>(Result<T> result, int cap, bool write)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        var expected = cap == SdkNativeProblemBoundaryScenario.ProblemBytes ? SdkNativeProblemBoundaryScenario.ExpectedProblem
            : Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost, P.Unavailable);
        await SdkNativeProblemBoundaryAssertions.EqualAsync(expected, result.Problem!);
    }
}
