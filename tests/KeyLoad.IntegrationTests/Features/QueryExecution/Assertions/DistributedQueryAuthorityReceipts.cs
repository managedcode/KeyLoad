using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityReceipts
{
    private const string PutKind = "putDocument";

    internal static async Task RequireAsync(DistributedQueryAuthorityContext context, DistributedQueryAuthorityState state,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OneAsync(context.Source, context.SourceOfficial,
            state.Seed.LocalCommand, state.Seed.LocalReceipt, state.Seed.Destination.Directory.ControlOwner, token), failures);
        var destination = state.Seed.Destination.Directory.Owners.Single(item =>
            item.Owner.PhysicalShardId != state.Seed.Destination.Directory.ControlOwner.PhysicalShardId).Owner;
        await ServerFailureObserver.ObserveAsync(() => OneAsync(context.Destination, context.DestinationOfficial,
            state.Seed.Destination.Command, state.Seed.Destination.Receipt, destination, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task OneAsync(KeyLoadClient sdk, McpOfficialClient official, CommandRequest command,
        CommitReceipt original, PhysicalShardRecord owner, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(original.Token.Position).IsGreaterThan(DistributedQueryAuthorityProtocol.EmptyPosition), failures);
        var expected = new CommitReceipt(command.CommandId,
            new(owner.Incarnation, command.Partition.AtomicPartitionId, original.Token.Position, owner.PlacementEpoch),
            [new MutationReceipt(PutKind, RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document, DistributedQueryAuthorityProtocol.FirstRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(expected, original), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.CallAsync(
                RemoteDocumentRf3Protocol.DocumentsCommit, command, token))).Value), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
