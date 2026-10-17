using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class AggregateSnapshotRaceCompetitors
{
    internal static async Task RunAsync(AggregateSnapshotRaceFixture fixture, CancellationToken token)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = fixture.Commands.Select(AggregateSnapshotRaceFixture.Operation).ToArray();
        var tasks = operations.Select(operation => Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return fixture.Database.Apply(operation);
        }, CancellationToken.None)).ToArray();
        start.SetResult();
        var results = new OperationResult?[tasks.Length];
        var failures = new List<Exception>();
        for (var index = 0; index < tasks.Length; index++)
        {
            var selected = index;
            await ServerFailureObserver.ObserveAsync(async () => results[selected] = await tasks[selected].ConfigureAwait(false), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        for (var index = 0; index < operations.Length; index++)
        {
            var result = results[index]!;
            var raw = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                fixture.Harness.Partition, AggregateSnapshotRaceFixture.Worker, operations[index].Id)))
                ?? throw new InvalidOperationException("The snapshot competitor outcome is missing.");
            fixture.Outcomes.Add((operations[index], result, raw));
        }
        await Assert.That(results.Count(result => result!.Error is null)).IsEqualTo(1);
        var winner = Array.FindIndex(results, result => result!.Error is null);
        fixture.Winner = fixture.Commands[winner];
        var loser = results[1 - winner]!;
        await Assert.That(loser.Json).IsNull();
        await Assert.That(loser.NativeValue).IsNull();
        await Assert.That(loser.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(loser.SafeDetail).IsEqualTo("The aggregate snapshot version has changed.");
    }
}
