using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextCapturedBudgetFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var seed = await TextOnlineSeedFixture.CreateAsync(fixture, token);
        await using var runtime = new TextOnlineTestRuntime(fixture);
        var request = new OnlineTextIndexMaintenanceRequest(Guid.NewGuid(), seed.Pin.Consumer,
            seed.Pin.Collection, seed.Pin.Field, seed.Pin.Generation, seed.Pin.NodeId, seed.Pin.Placement);
        var original = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        var policy = UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits with
        {
            MaxQueryReadBytes = sizeof(byte)
        });
        var budget = new ReadExecutionBudget(policy, fixture.Database.EvaluationClock, token);
        ICapturedTextRead? admitted = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            admitted = runtime.CaptureForBudgetTrial(fixture, TextBilingualAudit.Request(fixture.Partition, "hello"), budget);
            _ = admitted.CompleteSource();
        }, failures);
        if (admitted is not null)
        {
            ServerFailureObserver.Observe(admitted.Dispose, failures);
        }
        await Assert.That(failures).IsNotEmpty();
        await Assert.That(failures.SelectMany(Flatten).Any(error => error is KeyLoadException { Code: ErrorCode.BudgetExceeded })).IsTrue();
        if (failures.SelectMany(Flatten).Any(error => error is not KeyLoadException { Code: ErrorCode.BudgetExceeded }))
        {
            ServerFailureObserver.ThrowIfAny(failures);
        }
        await TextCapturedPublicFlow.CancelledCallerThenJoinedAsync(runtime, fixture, token);
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        var replay = await TextOnlineWholeFlow.RunAsync(fixture, runtime, request, token);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await TextBilingualAudit.VerifyAsync(runtime.Search, fixture.Partition, token);
        await runtime.DisposeAsync();
        await using var cold = new TextOnlineTestRuntime(fixture);
        await TextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
        var coldReplay = await TextOnlineWholeFlow.RunAsync(fixture, cold, request, token);
        await Assert.That(OrleansSerialization.Serialize(coldReplay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await TextBilingualAudit.VerifyAsync(cold.Search, fixture.Partition, token);
    }

    private static IEnumerable<Exception> Flatten(Exception error)
        => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error];
}
