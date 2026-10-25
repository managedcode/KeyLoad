using KeyLoad.Server.Search;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.Search;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class RequestCqrsTextCheckpointObservation
{
    private const string ObservationPrefix = "ORIGINAL_CHECKPOINT_CUT";

    internal static async Task RecordAsync(RequestCqrsHostedRuntimeFixture fixture,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var database = fixture.Database;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var budget = new ReadExecutionBudget(fixture.Clock, token);
        var options = UnitTextOptions.Execution();
        var root = Path.Combine(database.Directory, TextIncrementalProtocol.RootDirectory);
        var leaf = TextIncrementalEnrollmentFiles.Find(root, request, budget, options)
            ?? throw new InvalidOperationException();
        var original = TextIncrementalMetadata.ReadIntent(Path.Combine(root, leaf), options.Value.MaximumDiskBytes, budget);
        var command = original.CheckpointCommand;
        var issued = database.Database.CreateCommand(OperationKind.CommitProjectionBatch,
            command.CommandId, TextMaintenanceTestValues.Principal, default, OrleansSerialization.Serialize(command));
        var outcome = database.Database.ResolveOutcome(issued);
        var actual = outcome.Value as ProjectionBatchResult;
        var applied = database.Store.Read(view => OrleansSerialization.Deserialize<long>(
            view.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw new InvalidOperationException()));
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store).AsSpan().SequenceEqual(before)).IsTrue();
        await Console.Error.WriteLineAsync($"{ObservationPrefix} hasOutcome={actual is not null} error={Errors.Code(outcome.Problem).Value} checkpoint={actual?.Checkpoint} applied={applied} ackPosition={actual?.Receipt.Token.Position}").ConfigureAwait(false);
    }
}
