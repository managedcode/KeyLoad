using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationOriginalSessionFlow
{
    private const string PutDocumentKind = "putDocument";
    internal static async Task StartAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers writer,
        NativeActivationOriginalSessionState state, CancellationToken token)
    {
        var placement = await NativeActivationMigrationRf3Ownership.ReadPlacementAsync(administrator,
            state.Command.Partition, token);
        await Assert.That(state.Receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(state.Command.CommandId,
            new(placement.Incarnation, state.Command.Partition.AtomicPartitionId,
                state.Receipt.Token.Position, placement.PlacementEpoch),
            [new(PutDocumentKind, state.Reference.Collection, state.Reference.Id, NativeActivationRf3Protocol.CommittedRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await NativeActivationOriginalSessionConservation.RequireReceiptAsync(expected, state.Receipt);
        await ReadAsync(administrator, writer, state, NativeActivationRf3Protocol.CommittedRevision,
            RequestCqrsRf3Protocol.ChangedDocumentJson, token);
    }

    internal static Task ReadIfSelectedAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers writer,
        NativeActivationOriginalSessionState? state, CancellationToken token)
        => state is null ? Task.CompletedTask : ReadAsync(administrator, writer, state,
            NativeActivationRf3Protocol.CommittedRevision, RequestCqrsRf3Protocol.ChangedDocumentJson, token);

    internal static async Task ReadAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers writer,
        NativeActivationOriginalSessionState state, long revision, string json, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var route = 0; route < 4; route++)
        {
            var originalRoute = route;
            await ServerFailureObserver.ObserveAsync(() => NativeActivationOriginalSessionConservation.ObserveAsync(
                administrator, state, revision, json,
                () => NativeActivationOriginalSessionReads.HealthyAsync(state.Readers, state,
                    revision, json, originalRoute, token), token), failures);
            await ServerFailureObserver.ObserveAsync(() => NativeActivationOriginalSessionConservation.ObserveAsync(
                administrator, state, revision, json,
                () => NativeActivationOriginalSessionConservation.ReplayAsync(writer, state, originalRoute, token), token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
