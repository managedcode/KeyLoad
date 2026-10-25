using KeyLoad.Client;
using KeyLoad.FunctionalTests.OrleansExecution;
using ManagedCode.Communication;
using P = KeyLoad.FunctionalTests.ClientApi.SdkProblemBoundaryProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SdkProblemBoundaryStages
{
    internal static async Task RunAsync(SdkProblemBoundaryScenario state, KeyLoadClientKestrelServer server,
        SdkProblemBoundaryTasks tasks, bool declared, CancellationToken token)
    {
        var original = SdkProblemBoundaryAssertions.Capture(state);
        await SdkProblemBoundaryWire.RequireAsync(server, state, tasks, declared, token);
        await SdkProblemBoundaryAssertions.SameAsync(state, original);
        foreach (var cap in new[] { SdkProblemBoundaryScenario.ProblemBytes - P.Lookahead, SdkProblemBoundaryScenario.ProblemBytes })
        {
            var sdk = new KeyLoadClient(server.Client, P.Secret, UnitClientOptions.Execution(new()
            {
                MaximumProblemBodyBytes = cap
            }));
            await FailureAsync(await tasks.CompleteAsync(tasks.Retain(sdk.GetAsync(state.Forbidden, token))),
                cap, write: false);
            await SdkProblemBoundaryAssertions.SameAsync(state, original);
            await FailureAsync(await tasks.CompleteAsync(tasks.Retain(sdk.CommitAsync(state.Denied, token))),
                cap, write: true);
            await SdkProblemBoundaryAssertions.SameAsync(state, original);
        }
        var healthy = new KeyLoadClient(server.Client, P.Secret, UnitClientOptions.Execution());
        var read = await tasks.CompleteAsync(tasks.Retain(healthy.GetAsync(state.Allowed, token)));
        await Assert.That(read.IsSuccess).IsTrue();
        await Assert.That(Errors.Code(read.Problem).HasValue).IsFalse();
        await SdkProblemBoundaryAssertions.EqualAsync(new DocumentReadResult(
            new(state.Allowed, P.FirstRevision, P.SeedJson, false, [])), read.Value!);
        await SdkProblemBoundaryAssertions.SameAsync(state, original);
        state.HealthyToken = ExpectedCanonicalCommit.Next(state.Execution.Fixture.Database.Database, state.Healthy.Partition);
        var committed = await tasks.CompleteAsync(tasks.Retain(healthy.CommitAsync(state.Healthy, token)));
        await Assert.That(committed.IsSuccess).IsTrue();
        await Assert.That(Errors.Code(committed.Problem).HasValue).IsFalse();
        state.HealthyReceipt = committed.Value ?? throw new InvalidOperationException(P.Failure);
        await SdkProblemBoundaryAssertions.ReceiptAsync(state, state.Healthy, state.HealthyReceipt, root: false);
        await SdkProblemBoundaryAssertions.CurrentAsync(state);
        var after = SdkProblemBoundaryAssertions.Capture(state);
        var replay = await tasks.CompleteAsync(tasks.Retain(healthy.CommitAsync(state.Healthy, token)));
        await Assert.That(replay.IsSuccess).IsTrue();
        await SdkProblemBoundaryAssertions.EqualAsync(state.HealthyReceipt, replay.Value!);
        await SdkProblemBoundaryAssertions.SameAsync(state, after);
        var reply = await state.ReplaySeedAsync(token);
        await Assert.That(reply.Error).IsNull();
        await SdkProblemBoundaryAssertions.EqualAsync(state.SeedReceipt, ConnectionAssertions.Value<CommitReceipt>(reply));
        await SdkProblemBoundaryAssertions.SameAsync(state, after);
        var current = await tasks.CompleteAsync(tasks.Retain(healthy.GetAsync(state.Allowed, token)));
        await Assert.That(current.IsSuccess).IsTrue();
        await SdkProblemBoundaryAssertions.EqualAsync(new DocumentReadResult(
            new(state.Allowed, P.HealthyRevision, P.HealthyJson, false, [])), current.Value!);
        await SdkProblemBoundaryAssertions.SameAsync(state, after);
    }
    private static async Task FailureAsync<T>(Result<T> result, int cap, bool write)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        var expected = cap == SdkProblemBoundaryScenario.ProblemBytes ? SdkProblemBoundaryScenario.ExpectedProblem
            : Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost, P.Unavailable);
        await SdkProblemBoundaryAssertions.EqualAsync(expected, result.Problem!);
        var code = Errors.Code(result.Problem);
        await Assert.That(code.HasValue).IsTrue();
        await Assert.That(code.Value).IsEqualTo(Errors.Code(expected).Value);
    }
}
