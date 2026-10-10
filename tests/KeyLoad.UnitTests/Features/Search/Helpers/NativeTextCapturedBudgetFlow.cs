using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedBudgetFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var seed = await NativeTextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new NativeTextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var policy = UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits with { MaxQueryReadBytes = sizeof(byte) });
        var budget = new ReadExecutionBudget(policy, fixture.Database.EvaluationClock, token);
        ICapturedTextRead? admitted = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            admitted = runtime.CaptureForBudgetTrial(fixture, NativeTextBilingualAudit.Request(fixture.Partition, "hello"), budget);
            _ = admitted.CompleteSource();
        }, failures);
        if (admitted is not null)
        { ServerFailureObserver.Observe(admitted.Dispose, failures); }
        await Assert.That(failures).IsNotEmpty();
        await Assert.That(failures.SelectMany(Flatten).Any(error => error is KeyLoadException { Code: ErrorCode.BudgetExceeded })).IsTrue();
        if (failures.SelectMany(Flatten).Any(error => error is not KeyLoadException { Code: ErrorCode.BudgetExceeded }))
        { ServerFailureObserver.ThrowIfAny(failures); }
        await NativeTextCapturedPublicFlow.CancelledCallerThenJoinedAsync(runtime, fixture, token);
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        var replay = await NativeTextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await NativeTextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        await runtime.DisposeAsync();
        await using var cold = new NativeTextOnlineTestRuntime(fixture);
        await NativeTextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
        var coldReplay = await NativeTextOnlineWholeFlow.RunAsync(fixture, cold, request, token);
        await Assert.That(NativeSerialization.Serialize(coldReplay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await NativeTextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
    }

    private static IEnumerable<Exception> Flatten(Exception error)
        => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error];
}
