using KeyLoad.Core;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsNativeTextCheckpointObservation
{
    private const string ObservationPrefix = "KL029_ORIGINAL_CHECKPOINT_CUT";

    internal static async Task RecordAsync(RequestCqrsClusterFixture fixture,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var database = fixture.Database;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), fixture.Clock, token);
        var options = UnitNativeTextOptions.Execution();
        var root = Path.Combine(database.Directory, NativeTextIncrementalProtocol.RootDirectory);
        var leaf = NativeTextIncrementalEnrollmentFiles.Find(root, request, budget, options)
            ?? throw new InvalidOperationException();
        var original = NativeTextIncrementalMetadata.ReadIntent(Path.Combine(root, leaf), options.Value.MaximumDiskBytes, budget);
        var command = original.CheckpointCommand;
        var issued = database.Database.CreateNativeOperation(OperationKind.CommitProjectionBatch,
            command.CommandId, NativeTextMaintenanceTestValues.Principal, default, NativeSerialization.Serialize(command));
        var outcome = database.Database.ResolveOutcome(issued);
        var actual = outcome.NativeValue as ProjectionBatchResult;
        var applied = database.Store.Read(view => NativeSerialization.Deserialize<long>(
            view.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw new InvalidOperationException()));
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store).AsSpan().SequenceEqual(before)).IsTrue();
        await Console.Error.WriteLineAsync($"{ObservationPrefix} hasOutcome={actual is not null} error={outcome.Error} checkpoint={actual?.Checkpoint} applied={applied} ackPosition={actual?.Receipt.Token.Position}").ConfigureAwait(false);
    }
}
